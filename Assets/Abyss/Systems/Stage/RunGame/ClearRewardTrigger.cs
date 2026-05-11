using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// EndEffect2 프리팹에 런타임 부착되는 보상 상호작용 컴포넌트.
///
/// 표시 레이어:
///   1) 월드 아이콘 + 거리(Xm) — 항상 보임, 카메라를 향해 빌보드
///   2) 스크린 스페이스 "[F] 보상 수령" — 트리거 범위 진입 시
///
/// F 키 입력 흐름:
///   수령 확인 팝업(Addressable) → 아이템 지급 → 결과 화면(코드 생성) → StageMap 복귀
/// </summary>
public class ClearRewardTrigger : MonoBehaviour
{
    // ── Constants ──────────────────────────────────────────────
    private const float TriggerRadius    = 2.5f;
    private const float WorldIconHeight  = 2.8f;
    private const float WorldCanvasScale = 0.005f;

    // ── Private fields ─────────────────────────────────────────
    private GameRunSession _run;
    private List<(RuntimeItemData data, ItemSO so)> _rewards;
    private Transform _camTransform;
    private bool _playerInRange;
    private bool _rewarded;

    private GameObject _promptGO;
    private GameObject _worldIndicatorGO;
    private Image      _worldIconImg;
    private TextMeshProUGUI _distanceText;
    private Sprite     _rewardSprite;

    // 코드 생성 결과 화면
    private GameObject _resultScreenGO;
    private UniTaskCompletionSource _resultTcs;

    // ── Public Methods ─────────────────────────────────────────

    public void Initialize(GameRunSession run, List<(RuntimeItemData data, ItemSO so)> rewards)
    {
        _run     = run;
        _rewards = rewards;

        if (!TryGetComponent<SphereCollider>(out var col))
            col = gameObject.AddComponent<SphereCollider>();
        col.isTrigger = true;
        col.radius    = TriggerRadius;

        if (rewards != null && rewards.Count > 0)
            _rewardSprite = rewards[0].so?.icon ?? rewards[0].data?.icon;
    }

    // ── Lifecycle ──────────────────────────────────────────────

    private void Start()
    {
        _camTransform = Camera.main != null ? Camera.main.transform : null;

        if (_rewards != null && _rewards.Count > 0)
        {
            CreateWorldIndicator();
            CreateScreenPrompt();
        }
    }

    private void Update()
    {
        if (_worldIndicatorGO != null && !_rewarded)
            UpdateWorldIndicator();

        if (_rewarded || !_playerInRange || _rewards == null || _rewards.Count == 0) return;

        if (Input.GetKeyDown(KeyCode.F))
            OpenRewardFlowAsync().Forget();
    }

    private void OnDestroy()
    {
        if (_promptGO         != null) Destroy(_promptGO);
        if (_worldIndicatorGO != null) Destroy(_worldIndicatorGO);
        if (_resultScreenGO   != null) Destroy(_resultScreenGO);
        _resultTcs?.TrySetResult();
    }

    private void OnTriggerEnter(Collider other)
    {
        if (_rewarded) return;
        if (!IsPlayer(other)) return;
        _playerInRange = true;
        ShowPrompt(true);
    }

    private void OnTriggerExit(Collider other)
    {
        if (!IsPlayer(other)) return;
        _playerInRange = false;
        ShowPrompt(false);
    }

    // ── Reward Flow ────────────────────────────────────────────

    private async UniTaskVoid OpenRewardFlowAsync()
    {
        _rewarded = true;
        ShowPrompt(false);
        HideWorldIndicator();

        var ct = this.GetCancellationTokenOnDestroy();

        // 1단계: 수령 확인 팝업 (Addressable)
        var acquirePopup = await Managers.UI.ShowPopupUIAndGetAsync<UI_ClearReward>();
        if (acquirePopup == null)
        {
            GiveAllRewards();
        }
        else
        {
            try
            {
                await acquirePopup.WaitForConfirmAsync().AttachExternalCancellation(ct);
            }
            catch (OperationCanceledException)
            {
                Destroy(gameObject);
                return;
            }
            GiveAllRewards();
        }

        // 2단계: 아이템 확인 화면 (코드 생성 — Addressable 불필요)
        ShowResultScreen();
        try
        {
            await WaitForResultConfirmAsync().AttachExternalCancellation(ct);
        }
        catch (OperationCanceledException) { }
        finally
        {
            if (_resultScreenGO != null) Destroy(_resultScreenGO);
        }

        // 3단계: 스테이지 노드 클리어 → StageMap 복귀
        var spm = _run?.StagePointManager;
        if (spm != null && spm.CurrentPointId >= 0)
            spm.MarkCleared(spm.CurrentPointId);

        AppBootstrapper.Instance?.RequestLoad(Define.Scene.StageMap);
        Destroy(gameObject);
    }

    private void GiveAllRewards()
    {
        if (_run?.ItemInventory == null || _rewards == null) return;
        foreach (var (data, _) in _rewards)
        {
            if (data == null) continue;
            _run.ItemInventory.AddItem(data);
            _run.EffectManager?.OnItemPickup(data);
            Debug.Log($"[ClearRewardTrigger] 아이템 지급: {data.displayName}");
        }
    }

    // ── Result Screen (코드 생성) ──────────────────────────────

    private void ShowResultScreen()
    {
        _resultScreenGO = new GameObject("ResultScreen");

        var canvas = _resultScreenGO.AddComponent<Canvas>();
        canvas.renderMode   = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 20;
        _resultScreenGO.AddComponent<CanvasScaler>();
        _resultScreenGO.AddComponent<GraphicRaycaster>();

        // 배경 딤
        var dimGO  = new GameObject("Dim");
        dimGO.transform.SetParent(_resultScreenGO.transform, false);
        var dimImg = dimGO.AddComponent<Image>();
        dimImg.color = new Color(0f, 0f, 0f, 0.7f);
        var dimRT  = dimGO.GetComponent<RectTransform>();
        dimRT.anchorMin = Vector2.zero;
        dimRT.anchorMax = Vector2.one;
        dimRT.offsetMin = Vector2.zero;
        dimRT.offsetMax = Vector2.zero;

        // 패널
        var panelGO  = new GameObject("Panel");
        panelGO.transform.SetParent(_resultScreenGO.transform, false);
        var panelImg = panelGO.AddComponent<Image>();
        panelImg.color = new Color(0.1f, 0.1f, 0.15f, 0.97f);
        var panelRT  = panelGO.GetComponent<RectTransform>();
        panelRT.anchorMin        = new Vector2(0.5f, 0.5f);
        panelRT.anchorMax        = new Vector2(0.5f, 0.5f);
        panelRT.pivot            = new Vector2(0.5f, 0.5f);
        panelRT.sizeDelta        = new Vector2(640f, 360f);
        panelRT.anchoredPosition = Vector2.zero;

        // 타이틀
        var titleGO  = new GameObject("Title");
        titleGO.transform.SetParent(panelGO.transform, false);
        var titleTmp = titleGO.AddComponent<TextMeshProUGUI>();
        titleTmp.text      = "보상 획득!";
        titleTmp.fontSize  = 28f;
        titleTmp.fontStyle = FontStyles.Bold;
        titleTmp.alignment = TextAlignmentOptions.Center;
        titleTmp.color     = new Color(1f, 0.84f, 0f);
        var titleRT  = titleGO.GetComponent<RectTransform>();
        titleRT.anchorMin        = new Vector2(0f, 1f);
        titleRT.anchorMax        = new Vector2(1f, 1f);
        titleRT.pivot            = new Vector2(0.5f, 1f);
        titleRT.sizeDelta        = new Vector2(0f, 60f);
        titleRT.anchoredPosition = new Vector2(0f, -10f);

        // 아이템 카드 컨테이너 (HorizontalLayout)
        var containerGO = new GameObject("ItemCardContainer");
        containerGO.transform.SetParent(panelGO.transform, false);
        var hLayout = containerGO.AddComponent<HorizontalLayoutGroup>();
        hLayout.spacing              = 16f;
        hLayout.childAlignment       = TextAnchor.MiddleCenter;
        hLayout.childForceExpandWidth  = false;
        hLayout.childForceExpandHeight = false;
        var containerRT = containerGO.GetComponent<RectTransform>();
        containerRT.anchorMin        = new Vector2(0f, 0.2f);
        containerRT.anchorMax        = new Vector2(1f, 0.85f);
        containerRT.offsetMin        = new Vector2(20f, 0f);
        containerRT.offsetMax        = new Vector2(-20f, 0f);

        // 아이템 카드 생성
        if (_rewards != null)
        {
            foreach (var (data, so) in _rewards)
                SpawnItemCard(containerGO.transform, data, so);
        }

        // 돌아가기 버튼
        var btnGO  = new GameObject("BackBtn");
        btnGO.transform.SetParent(panelGO.transform, false);
        var btnImg = btnGO.AddComponent<Image>();
        btnImg.color = new Color(0.18f, 0.44f, 0.76f);
        var btn    = btnGO.AddComponent<Button>();
        btn.targetGraphic = btnImg;
        btn.onClick.AddListener(OnResultConfirm);
        var btnRT  = btnGO.GetComponent<RectTransform>();
        btnRT.anchorMin        = new Vector2(0.5f, 0f);
        btnRT.anchorMax        = new Vector2(0.5f, 0f);
        btnRT.pivot            = new Vector2(0.5f, 0f);
        btnRT.sizeDelta        = new Vector2(180f, 48f);
        btnRT.anchoredPosition = new Vector2(0f, 16f);

        var btnTextGO  = new GameObject("BtnText");
        btnTextGO.transform.SetParent(btnGO.transform, false);
        var btnTmp = btnTextGO.AddComponent<TextMeshProUGUI>();
        btnTmp.text      = "돌아가기";
        btnTmp.fontSize  = 20f;
        btnTmp.alignment = TextAlignmentOptions.Center;
        btnTmp.color     = Color.white;
        var btnTextRT    = btnTextGO.GetComponent<RectTransform>();
        btnTextRT.anchorMin = Vector2.zero;
        btnTextRT.anchorMax = Vector2.one;
        btnTextRT.offsetMin = Vector2.zero;
        btnTextRT.offsetMax = Vector2.zero;
    }

    private void SpawnItemCard(Transform parent, RuntimeItemData data, ItemSO so)
    {
        var cardGO = new GameObject("ItemCard");
        cardGO.transform.SetParent(parent, false);
        var cardRT = cardGO.AddComponent<RectTransform>();
        cardRT.sizeDelta = new Vector2(140f, 200f);
        cardGO.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0f);

        // 등급 배경
        var bgGO  = new GameObject("RarityBG");
        bgGO.transform.SetParent(cardGO.transform, false);
        var bgImg = bgGO.AddComponent<Image>();
        bgImg.color = GetRarityColor(data?.rarity ?? ItemRarity.Common);
        var bgRT  = bgGO.GetComponent<RectTransform>();
        bgRT.anchorMin        = new Vector2(0.5f, 0.5f);
        bgRT.anchorMax        = new Vector2(0.5f, 0.5f);
        bgRT.pivot            = new Vector2(0.5f, 0.5f);
        bgRT.sizeDelta        = new Vector2(100f, 100f);
        bgRT.anchoredPosition = new Vector2(0f, 42f);

        // 아이콘 (ItemSO.icon 직접 참조 — 상점과 동일 방식)
        var sprite = so?.icon ?? data?.icon;
        if (sprite != null)
        {
            var iconGO  = new GameObject("Icon");
            iconGO.transform.SetParent(bgGO.transform, false);
            var iconImg = iconGO.AddComponent<Image>();
            iconImg.sprite              = sprite;
            iconImg.preserveAspect      = true;
            var iconRT = iconGO.GetComponent<RectTransform>();
            iconRT.anchorMin = Vector2.zero;
            iconRT.anchorMax = Vector2.one;
            iconRT.offsetMin = new Vector2(8f, 8f);
            iconRT.offsetMax = new Vector2(-8f, -8f);
        }

        // 블록 그리드 모양 미리보기
        if (data != null && data.shapeId > 0)
        {
            var shapeEntry = Managers.BlockData?.GetShape(data.shapeId);
            if (shapeEntry != null)
            {
                var offsets = BlockDataManager.ParseCellOffsets(shapeEntry);
                SpawnShapeMini(cardGO.transform, offsets, new Vector2(0f, -22f));
            }
        }

        // 아이템 이름
        var nameGO  = new GameObject("Name");
        nameGO.transform.SetParent(cardGO.transform, false);
        var nameTmp = nameGO.AddComponent<TextMeshProUGUI>();
        nameTmp.text      = data?.displayName ?? so?.name ?? "";
        nameTmp.fontSize  = 13f;
        nameTmp.alignment = TextAlignmentOptions.Center;
        nameTmp.color     = Color.white;
        var nameRT  = nameGO.GetComponent<RectTransform>();
        nameRT.anchorMin        = new Vector2(0.5f, 0.5f);
        nameRT.anchorMax        = new Vector2(0.5f, 0.5f);
        nameRT.pivot            = new Vector2(0.5f, 0.5f);
        nameRT.sizeDelta        = new Vector2(130f, 25f);
        nameRT.anchoredPosition = new Vector2(0f, -68f);

        // 등급 텍스트
        var rarityGO  = new GameObject("Rarity");
        rarityGO.transform.SetParent(cardGO.transform, false);
        var rarityTmp = rarityGO.AddComponent<TextMeshProUGUI>();
        var rarity    = data?.rarity ?? ItemRarity.Common;
        rarityTmp.text      = $"<color={GetRarityHex(rarity)}>{rarity}</color>";
        rarityTmp.fontSize  = 11f;
        rarityTmp.alignment = TextAlignmentOptions.Center;
        rarityTmp.color     = Color.white;
        var rarityRT  = rarityGO.GetComponent<RectTransform>();
        rarityRT.anchorMin        = new Vector2(0.5f, 0.5f);
        rarityRT.anchorMax        = new Vector2(0.5f, 0.5f);
        rarityRT.pivot            = new Vector2(0.5f, 0.5f);
        rarityRT.sizeDelta        = new Vector2(130f, 20f);
        rarityRT.anchoredPosition = new Vector2(0f, -88f);
    }

    private static void SpawnShapeMini(Transform parent, Vector2Int[] offsets, Vector2 anchoredPos)
    {
        if (offsets == null || offsets.Length == 0) return;

        int minC = int.MaxValue, maxC = int.MinValue;
        int minR = int.MaxValue, maxR = int.MinValue;

        foreach (var o in offsets)
        {
            int c = o.x;
            int r = -o.y;
            if (c < minC) minC = c;
            if (c > maxC) maxC = c;
            if (r < minR) minR = r;
            if (r > maxR) maxR = r;
        }

        int cols = maxC - minC + 1;
        int rows = maxR - minR + 1;

        const float CellSize = 9f;
        const float Gap      = 1f;
        const float Step     = CellSize + Gap;

        float totalW = cols * CellSize + (cols - 1) * Gap;
        float totalH = rows * CellSize + (rows - 1) * Gap;

        var containerGO = new GameObject("ShapeMini");
        containerGO.transform.SetParent(parent, false);
        var containerRT = containerGO.AddComponent<RectTransform>();
        containerRT.anchorMin        = new Vector2(0.5f, 0.5f);
        containerRT.anchorMax        = new Vector2(0.5f, 0.5f);
        containerRT.pivot            = new Vector2(0.5f, 0.5f);
        containerRT.sizeDelta        = new Vector2(totalW, totalH);
        containerRT.anchoredPosition = anchoredPos;

        // 빈 셀 배경 (어두운 격자)
        for (int r = 0; r < rows; r++)
        {
            for (int c = 0; c < cols; c++)
            {
                var bgCell = new GameObject("BgCell");
                bgCell.transform.SetParent(containerGO.transform, false);
                bgCell.AddComponent<Image>().color = new Color(0.12f, 0.12f, 0.18f, 0.9f);
                var rt = bgCell.GetComponent<RectTransform>();
                rt.anchorMin        = new Vector2(0f, 1f);
                rt.anchorMax        = new Vector2(0f, 1f);
                rt.pivot            = new Vector2(0f, 1f);
                rt.sizeDelta        = new Vector2(CellSize, CellSize);
                rt.anchoredPosition = new Vector2(c * Step, -r * Step);
            }
        }

        // 채워진 셀 (파란색)
        var fillColor = new Color(0.35f, 0.65f, 1f, 0.95f);
        foreach (var o in offsets)
        {
            int c = o.x - minC;
            int r = -o.y - minR;

            var cell = new GameObject("Cell");
            cell.transform.SetParent(containerGO.transform, false);
            cell.AddComponent<Image>().color = fillColor;
            var rt = cell.GetComponent<RectTransform>();
            rt.anchorMin        = new Vector2(0f, 1f);
            rt.anchorMax        = new Vector2(0f, 1f);
            rt.pivot            = new Vector2(0f, 1f);
            rt.sizeDelta        = new Vector2(CellSize, CellSize);
            rt.anchoredPosition = new Vector2(c * Step, -r * Step);
        }
    }

    private UniTask WaitForResultConfirmAsync()
    {
        _resultTcs = new UniTaskCompletionSource();
        return _resultTcs.Task;
    }

    private void OnResultConfirm() => _resultTcs?.TrySetResult();

    // ── World Indicator (아이콘 + 거리) ───────────────────────

    private void CreateWorldIndicator()
    {
        _worldIndicatorGO = new GameObject("WorldIndicator");
        _worldIndicatorGO.transform.SetParent(transform, false);
        _worldIndicatorGO.transform.localPosition = Vector3.up * WorldIconHeight;

        var canvas = _worldIndicatorGO.AddComponent<Canvas>();
        canvas.renderMode  = RenderMode.WorldSpace;
        canvas.worldCamera = Camera.main;

        var rt = _worldIndicatorGO.GetComponent<RectTransform>();
        rt.sizeDelta  = new Vector2(200f, 140f);
        rt.localScale = Vector3.one * WorldCanvasScale;

        var iconBgGO  = new GameObject("IconBG");
        iconBgGO.transform.SetParent(_worldIndicatorGO.transform, false);
        var iconBgImg = iconBgGO.AddComponent<Image>();
        iconBgImg.color = new Color(0.1f, 0.1f, 0.1f, 0.75f);
        var iconBgRT  = iconBgGO.GetComponent<RectTransform>();
        iconBgRT.anchorMin        = new Vector2(0.5f, 0.5f);
        iconBgRT.anchorMax        = new Vector2(0.5f, 0.5f);
        iconBgRT.pivot            = new Vector2(0.5f, 0.5f);
        iconBgRT.sizeDelta        = new Vector2(90f, 90f);
        iconBgRT.anchoredPosition = new Vector2(0f, 25f);

        var iconGO = new GameObject("Icon");
        iconGO.transform.SetParent(iconBgGO.transform, false);
        _worldIconImg        = iconGO.AddComponent<Image>();
        _worldIconImg.sprite = _rewardSprite;
        _worldIconImg.gameObject.SetActive(_rewardSprite != null);
        var iconRT = iconGO.GetComponent<RectTransform>();
        iconRT.anchorMin = Vector2.zero;
        iconRT.anchorMax = Vector2.one;
        iconRT.offsetMin = new Vector2(8f, 8f);
        iconRT.offsetMax = new Vector2(-8f, -8f);

        var distGO = new GameObject("DistanceText");
        distGO.transform.SetParent(_worldIndicatorGO.transform, false);
        _distanceText           = distGO.AddComponent<TextMeshProUGUI>();
        _distanceText.fontSize  = 28f;
        _distanceText.alignment = TextAlignmentOptions.Center;
        _distanceText.color     = Color.white;
        _distanceText.text      = "";
        var distRT = distGO.GetComponent<RectTransform>();
        distRT.anchorMin        = new Vector2(0.5f, 0.5f);
        distRT.anchorMax        = new Vector2(0.5f, 0.5f);
        distRT.pivot            = new Vector2(0.5f, 0.5f);
        distRT.sizeDelta        = new Vector2(160f, 40f);
        distRT.anchoredPosition = new Vector2(0f, -35f);
    }

    private void UpdateWorldIndicator()
    {
        if (_camTransform != null)
            _worldIndicatorGO.transform.rotation = _camTransform.rotation;

        if (_distanceText == null) return;
        var playerPos = _run?.Player?.transform?.position;
        if (playerPos == null) return;
        float dist = Vector3.Distance(transform.position, playerPos.Value);
        _distanceText.text = $"{Mathf.RoundToInt(dist)}m";
    }

    private void HideWorldIndicator()
    {
        if (_worldIndicatorGO != null)
            _worldIndicatorGO.SetActive(false);
    }

    // ── Screen Prompt ([F] 보상 수령) ─────────────────────────

    private void CreateScreenPrompt()
    {
        _promptGO = new GameObject("RewardPrompt_Screen");

        var canvas = _promptGO.AddComponent<Canvas>();
        canvas.renderMode   = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 5;
        _promptGO.AddComponent<CanvasScaler>();
        _promptGO.AddComponent<GraphicRaycaster>();

        var panelGO  = new GameObject("Panel");
        panelGO.transform.SetParent(_promptGO.transform, false);
        var panelImg = panelGO.AddComponent<Image>();
        panelImg.color = new Color(0f, 0f, 0f, 0.55f);
        var panelRT  = panelGO.GetComponent<RectTransform>();
        panelRT.anchorMin        = new Vector2(0.5f, 0.35f);
        panelRT.anchorMax        = new Vector2(0.5f, 0.35f);
        panelRT.pivot            = new Vector2(0.5f, 0.5f);
        panelRT.sizeDelta        = new Vector2(280f, 50f);
        panelRT.anchoredPosition = Vector2.zero;

        var textGO = new GameObject("PromptText");
        textGO.transform.SetParent(panelGO.transform, false);
        var tmp    = textGO.AddComponent<TextMeshProUGUI>();
        tmp.text      = "<color=#FFD700>[F]</color>  보상 수령";
        tmp.fontSize  = 20f;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.color     = Color.white;
        var textRT = textGO.GetComponent<RectTransform>();
        textRT.anchorMin = Vector2.zero;
        textRT.anchorMax = Vector2.one;
        textRT.offsetMin = Vector2.zero;
        textRT.offsetMax = Vector2.zero;

        _promptGO.SetActive(false);
    }

    private void ShowPrompt(bool show)
    {
        if (_promptGO == null) return;
        _promptGO.SetActive(show && !_rewarded);
    }

    // ── Helpers ────────────────────────────────────────────────

    private static Color GetRarityColor(ItemRarity rarity) => rarity switch
    {
        ItemRarity.Common => new Color(0.165f, 0.165f, 0.290f),
        ItemRarity.Rare   => new Color(0.0f,   0.25f,  0.35f),
        ItemRarity.Epic   => new Color(0.25f,  0.1f,   0.35f),
        _                 => new Color(0.165f, 0.165f, 0.290f),
    };

    private static string GetRarityHex(ItemRarity rarity) => rarity switch
    {
        ItemRarity.Common => "#FFFFFF",
        ItemRarity.Rare   => "#00FFFF",
        ItemRarity.Epic   => "#CC66FF",
        _                 => "#FFFFFF",
    };

    private static bool IsPlayer(Collider col)
        => col.GetComponentInParent<PlayerController>() != null;
}
