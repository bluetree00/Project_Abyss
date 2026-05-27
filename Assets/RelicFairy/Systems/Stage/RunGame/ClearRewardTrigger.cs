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
///   수령 확인 팝업(Addressable) → 아이템 지급(팝업 + 그리드 열기) → 노드 클리어 마킹
///   ※ 스테이지 전환은 별도 메커니즘이 담당한다.
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
    private bool _isBossRoom;

    private GameObject _promptGO;
    private GameObject _worldIndicatorGO;
    private Image      _worldIconImg;
    private TextMeshProUGUI _distanceText;
    private Sprite     _rewardSprite;


    // ── Public Methods ─────────────────────────────────────────

    public void Initialize(GameRunSession run, List<(RuntimeItemData data, ItemSO so)> rewards, bool isBossRoom = false)
    {
        _run        = run;
        _rewards    = rewards;
        _isBossRoom = isBossRoom;

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
            try { await GiveAllRewardsWithPopupAsync(ct); }
            catch (OperationCanceledException) { return; }
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
            try { await GiveAllRewardsWithPopupAsync(ct); }
            catch (OperationCanceledException) { return; }
        }

        if (_isBossRoom && _run != null)
        {
            _run.EnterChapterClear();
            bool advanced = _run.AdvanceToNextChapter();
            Debug.Log($"[ClearRewardTrigger] 보스방 클리어 — 챕터 전환 {(advanced ? "성공" : "마지막 챕터")}");
        }

        // 방 클리어 시점 저장 (플레이어는 현재 존 위치 + 게이트 선택지 유지 상태로 재개)
        var rp = RunProgressManager.Instance;
        if (rp != null && _run != null && _run.IsRunning)
        {
            try { await rp.SaveAsync(_run, rp.ActiveSlotIndex); }
            catch (OperationCanceledException) { return; }
        }

        // 그리드 패널이 열려 있으면 닫힐 때까지 대기 — 열려 있는 동안 게이트를 활성화하면
        // 존 선택 UI가 그리드 위에 겹쳐 표시된다.
        try
        {
            await UniTask.WaitUntil(
                () => UI_GridPanel.Instance == null || !UI_GridPanel.Instance.IsOpen,
                cancellationToken: ct);
        }
        catch (OperationCanceledException) { return; }

        // 존 단위 진행: 존 클리어 게이트 활성화 (보스방 제외)
        // 플레이어가 게이트로 이동하면 ZoneExitGate가 ShowZoneSelectionAsync를 호출한다.
        if (!_isBossRoom)
        {
            var zoneProgression = _run?.ZoneProgression;
            zoneProgression?.EnableExitGateForZone(zoneProgression.CurrentZoneIndex);
        }

        Destroy(gameObject);
    }

    /// <summary>
    /// 각 보상 아이템을 UI_ItemAcquisitionPopup으로 순서대로 표시.
    /// [그리드 열기] → 보관함 추가, [거부] → 폐기.
    /// Popup 로드 실패 시 자동으로 보관함에 추가.
    /// </summary>
    private async UniTask GiveAllRewardsWithPopupAsync(System.Threading.CancellationToken ct)
    {
        if (_run?.ItemInventory == null || _rewards == null) return;

        foreach (var (data, _) in _rewards)
        {
            if (data == null) continue;

            _run.EffectManager?.OnItemPickup(data);

            // 팝업 표시 — 실패하면 자동 추가
            var popup = await Managers.UI.ShowPopupUIAndGetAsync<UI_ItemAcquisitionPopup>();
            if (popup == null)
            {
                _run.ItemInventory.AddToStaging(data);
                Debug.Log($"[ClearRewardTrigger] 팝업 로드 실패 — 자동 추가: {data.displayName}");
                continue;
            }

            popup.Setup(data, _run.ItemInventory);

            // 버튼 클릭 즉시 resolve — 0.14s 닫기 애니메이션을 기다리지 않는다
            await popup.WaitForInteractionAsync(ct);

        }
    }

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

    private static bool IsPlayer(Collider col)
        => col.GetComponentInParent<PlayerController>() != null;
}
