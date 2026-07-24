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
    // 보상 획득 반경. 2.5m는 정확히 그 위에 올라서야 잡히는 수준이라, 클리어 후 보상을 주우려고
    // 위치를 미세조정하는 불편이 있었다 — 지나가듯 스쳐도 잡히도록 넉넉히 잡는다.
    private const float TriggerRadius    = 4.5f;
    private const float WorldIconHeight  = 2.8f;
    private const float WorldCanvasScale = 0.005f;

    // ── Private fields ─────────────────────────────────────────
    private GameRunSession _run;
    private List<(RuntimeItemData data, ItemSO so)> _rewards;
    private Transform _camTransform;
    private bool _playerInRange;
    private bool _rewarded;
    private bool _isBossRoom;
    private bool _isChoice;      // true = 후보 중 1개 선택(룬 선택 팝업)
    private int  _choiceRounds = 1;  // 3지선다를 몇 번 반복할지(챌린지 다중 보상)

    /// <summary>선택을 넘겼을 때 주는 원석 수(밸런스 값).</summary>
    private const int SkipOreReward = 1;

    private GameObject _promptGO;
    private GameObject _worldIndicatorGO;
    private Image      _worldIconImg;
    private TextMeshProUGUI _distanceText;
    private Sprite     _rewardSprite;


    // ── Public Methods ─────────────────────────────────────────

    /// <param name="isChoice">
    /// true면 <paramref name="rewards"/>를 <b>선택 후보</b>로 보고 고르게 한다(룬 선택 팝업).
    /// false면 전부 순차 지급한다(현재 룬 경로에서는 쓰이지 않는다 — 룬은 언제나 3지선다).
    /// </param>
    /// <param name="choiceRounds">
    /// 3지선다를 몇 번 반복할지. <paramref name="rewards"/>를 이 수만큼 균등 분할해 라운드마다 한 벌씩 제시한다.
    /// 챌린지 다중 보상(개수 N)이 N번의 3지선다가 되는 지점 — 보상 개수는 그대로 두고 획득 경험만 통일한다.
    /// </param>
    public void Initialize(GameRunSession run, List<(RuntimeItemData data, ItemSO so)> rewards,
                           bool isBossRoom = false, bool isChoice = false, int choiceRounds = 1)
    {
        _run          = run;
        _rewards      = rewards;
        _isBossRoom   = isBossRoom;
        _isChoice     = isChoice;
        _choiceRounds = Mathf.Max(1, choiceRounds);

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

        // 수령 확인 팝업(UI_ClearReward)은 뺐다 — [F]로 이미 "받겠다"고 누른 뒤라
        // 같은 질문을 한 번 더 하는 셈이었고, 실제 보상 화면(룬 선택/획득)이 바로 뒤에 또 뜬다.
        // 클릭 두 번이 늘 뿐 정보가 없어, 곧장 보상 지급 화면으로 넘어간다.
        try { await GiveAllRewardsWithPopupAsync(ct); }
        catch (OperationCanceledException) { return; }

        // [보스 후처리 이관] 보스 클리어의 드래프트·런클리어·챕터 전환은 모두
        // GameRunBootstrapper.OnBossRoomClearedHandler(NotifyBossRoomCleared 구독)가 전담한다.
        // 보스방은 이제 이 트리거를 스폰하지 않으므로(RoomClearGate 참조) _isBossRoom 분기는 여기서 다루지 않는다.

        // 방 경계 저장은 로컬 권위(RunFlowController.SaveRunState)가 담당하므로 여기서는 별도 저장하지 않는다.

        // 그리드 패널이 열려 있으면 닫힐 때까지 대기 — 열려 있는 동안 게이트를 활성화하면
        // 존 선택 UI가 그리드 위에 겹쳐 표시된다.
        try
        {
            await UniTask.WaitUntil(
                () => UI_GridPanel.Instance == null || !UI_GridPanel.Instance.IsOpen,
                cancellationToken: ct);
        }
        catch (OperationCanceledException) { return; }

        // 절차 진행: RunFlowController가 출구 게이트를 담당하므로 레거시 존 클리어 게이트는 활성화하지 않는다.
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

        if (_isChoice)
        {
            await ShowRuneSelectAsync(ct);
            return;
        }

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

    /// <summary>
    /// 룬 선택 — 후보를 <see cref="_choiceRounds"/>벌로 나눠 라운드마다 1개씩 고르게 한다.
    /// 일반 클리어는 1라운드, 챌린지 다중 보상은 개수만큼 라운드가 돈다.
    /// </summary>
    private async UniTask ShowRuneSelectAsync(System.Threading.CancellationToken ct)
    {
        int rounds  = Mathf.Clamp(_choiceRounds, 1, _rewards.Count);
        int perRound = Mathf.Max(1, _rewards.Count / rounds);

        for (int r = 0; r < rounds; r++)
        {
            int start = r * perRound;
            if (start >= _rewards.Count) break;

            // 마지막 라운드는 나눠떨어지지 않고 남은 후보를 전부 가져간다(후보 유실 방지).
            int len = (r == rounds - 1) ? _rewards.Count - start
                                        : Mathf.Min(perRound, _rewards.Count - start);

            await ShowOneRuneChoiceAsync(_rewards.GetRange(start, len), ct);
        }
    }

    /// <summary>
    /// 3지선다 1회. 넘기면 원석으로 환원한다.
    /// 팝업 로드 실패 시 첫 후보를 자동 지급해 보상이 증발하지 않게 한다.
    /// </summary>
    private async UniTask ShowOneRuneChoiceAsync(
        List<(RuntimeItemData data, ItemSO so)> candidates, System.Threading.CancellationToken ct)
    {
        if (candidates == null || candidates.Count == 0) return;

        var popup = await Managers.UI.ShowPopupUIAndGetAsync<UI_RuneSelectPopup>();
        if (popup == null)
        {
            var fallback = candidates[0].data;
            if (fallback != null)
            {
                _run.EffectManager?.OnItemPickup(fallback);
                _run.ItemInventory.AddToStaging(fallback);
                Debug.LogWarning($"[ClearRewardTrigger] 선택 팝업 로드 실패 — 첫 후보 자동 지급: {fallback.displayName}");
            }
            return;
        }

        popup.Setup(candidates, _run.ItemInventory);
        await popup.WaitForInteractionAsync(ct);

        if (popup.Skipped)
        {
            // 넘기기 보상 — 원석. 죽은 화폐(RuneOre)에 생산 경로를 주는 지점이기도 하다.
            _run.FuelBank?.Add(FuelKind.RuneOre, SkipOreReward);
            Debug.Log($"[ClearRewardTrigger] 룬 선택 넘김 — 원석 +{SkipOreReward}");
            return;
        }

        if (popup.Result != null)
            _run.EffectManager?.OnItemPickup(popup.Result);   // 획득 훅은 실제로 고른 것에만
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
