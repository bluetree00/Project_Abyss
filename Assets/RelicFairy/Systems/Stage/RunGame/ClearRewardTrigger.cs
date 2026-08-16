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

    /// <summary>차징 상승 SFX의 시작 피치. 종료 피치는 등급 스펙(TierSpec.SfxPitch)이 정한다.</summary>
    private const float ChargeStartPitch = 0.85f;
    /// <summary>다중 라운드(챌린지 보상)에서 2라운드부터 차징 길이 배율.</summary>
    private const float MultiRoundChargeScale = 0.4f;

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
        if (UIInputGate.Blocked) return;

        if (Input.GetKeyDown(KeyCode.F))
            OpenRewardFlowAsync().Forget();
    }

    private void OnDestroy()
    {
        if (_promptGO         != null) Destroy(_promptGO);
        if (_worldIndicatorGO != null) Destroy(_worldIndicatorGO);
        // 외부 destroy 시 GridPanel이 열린 채 timeScale=0 고착 방지
        if (UI_GridPanel.Instance != null && UI_GridPanel.Instance.IsOpen)
            UI_GridPanel.Instance.Close();
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

        // 기대감 빌드업 — 반드시 팝업이 열리기 <b>전</b>에. 팝업이 열리면 timeScale=0이라
        // 카메라·슬로우모·히트스톱이 전부 무효가 된다(구현설계_보상공개연출 §A-5).
        try { await PlayChargeUpAsync(ct); }
        catch (OperationCanceledException) { return; }

        // 수령 확인 팝업(UI_ClearReward)은 뺐다 — [F]로 이미 "받겠다"고 누른 뒤라
        // 같은 질문을 한 번 더 하는 셈이었고, 실제 보상 화면(룬 선택/획득)이 바로 뒤에 또 뜬다.
        // 클릭 두 번이 늘 뿐 정보가 없어, 곧장 보상 지급 화면으로 넘어간다.
        try { await GiveAllRewardsWithPopupAsync(ct); }
        catch (OperationCanceledException) { return; }
        catch (Exception e)
        {
            Debug.LogError($"[ClearRewardTrigger] 보상 지급 중 예외: {e}");
            UI_GridPanel.Instance?.Close();
            Destroy(gameObject);
            return;
        }

        // [보스 후처리 이관] 보스 클리어의 드래프트·런클리어·챕터 전환은 모두
        // GameRunBootstrapper.OnBossRoomClearedHandler(NotifyBossRoomCleared 구독)가 전담한다.
        // 보스방은 이제 이 트리거를 스폰하지 않으므로(RoomClearGate 참조) _isBossRoom 분기는 여기서 다루지 않는다.

        // 보상 확정 → 즉시 저장. 방 경계 저장(SaveRunState)은 방 '입장' 시점이고 클리어 저장은
        // 보상 지급 '전'에 끝나 있어, 여기서 안 하면 방금 받은 룬·아이템이 다음 방 입장까지 미저장으로 남는다.
        // 수령 확정 → '미수령' 플래그를 내린 뒤 저장. 순서가 뒤바뀌면 이미 받은 보상이
        // pending=true인 채 저장돼 이어하기에서 한 번 더 지급된다(이중지급).
        RunFlowController.Active?.NotifyClearRewardClaimed();
        RunFlowController.Active?.SaveNow("clear-reward");

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
    /// [F] ~ 팝업 오픈 사이 0~0.55s의 기대감 구간(§C-1 Beat 1). 후보 중 <b>최고 등급</b>이 강도를 정한다.
    ///
    /// 여기가 카메라·슬로우모를 쓸 수 있는 유일한 구간이다 — 팝업이 열리는 순간 Pause(0f)가 잡혀
    /// 그 뒤로는 UI 로컬 트윈과 VolumePulse만 살아남는다.
    /// 슬로우 요청은 <c>finally</c>에서 반드시 해제한다(미해제 시 팝업 종료 후에도 슬로우가 남는다).
    /// </summary>
    private async UniTask PlayChargeUpAsync(System.Threading.CancellationToken ct)
    {
        var tier = RewardPresentation.MaxRarity(_rewards);
        var spec = RewardPresentation.For(tier);

        // 연속 라운드 감쇠 — 같은 연출 3연타는 지루함이 된다(§C-1 스킵 규칙).
        float duration = spec.ChargeDuration * (_choiceRounds > 1 ? MultiRoundChargeScale : 1f);
        if (duration <= 0f) return;

        bool slowed = false;
        try
        {
            if (spec.SlowMotionScale < 1f)
            {
                TimeScaleArbiter.Acquire(this, spec.SlowMotionScale, TimeScaleArbiter.Priority.SlowMotion);
                slowed = true;
            }

            if (tier == ItemRarity.Legendary)
                HitFeelService.CameraShake(0.06f, 0.20f);

            // 상승 SFX — 신규 클립 없이 기존 1클립을 피치 램프로 쓴다(§C-4 "피치 인자로 무료 티어링").
            Managers.Sound?.PlayEffectAsync(SoundKey.Sfx.UiButton, 0.5f, ChargeStartPitch).Forget();

            // 보상 오브젝트 떨림 — 내용 공개 전 상자가 떨리는 신호(각성 사전 신호).
            Vector3 basePos = transform.position;
            float t = 0f;
            while (t < duration)
            {
                t += Time.unscaledDeltaTime;
                float ramp = t / duration;
                if (spec.ShakeAmplitude > 0f)
                {
                    float amp = spec.ShakeAmplitude * ramp;
                    transform.position = basePos + new Vector3(
                        Mathf.Sin(t * 62f) * amp, Mathf.Sin(t * 47f) * amp * 0.6f, Mathf.Cos(t * 55f) * amp);
                }
                await UniTask.Yield(PlayerLoopTiming.Update, ct);
            }
            transform.position = basePos;

            Managers.Sound?.PlayEffectAsync(SoundKey.Sfx.UiButton, 0.6f, spec.SfxPitch).Forget();
        }
        finally
        {
            // 취소·파괴 어느 경로로 빠져도 timeScale 누수가 없게(§C-1 · GameRunBootstrapper 동일 패턴).
            if (slowed) TimeScaleArbiter.Release(this);
        }
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

        var interactionTask = popup.WaitForInteractionAsync(ct);
        popup.Setup(candidates, _run.ItemInventory);
        await interactionTask;

        if (popup.Skipped)
        {
            // 넘기기 보상 — 원석. 죽은 화폐(RuneOre)에 생산 경로를 주는 지점이기도 하다.
            _run.FuelBank?.Add(FuelKind.RuneOre, SkipOreReward);
            Debug.Log($"[ClearRewardTrigger] 룬 선택 넘김 — 원석 +{SkipOreReward}");
            return;
        }

        if (popup.Result != null)
            _run.EffectManager?.OnItemPickup(popup.Result);   // 획득 훅은 실제로 고른 것에만

        // 다중 라운드 보상: 다음 라운드 팝업을 열기 전에 그리드 패널이 닫힐 때까지 대기.
        // 대기 없이 즉시 다음 라운드로 진입하면 그리드 패널 열린 채로 새 팝업이 UIManager 스택에
        // 쌓여 UIManager·GridPanel 이 동시에 Pause를 점유하고, 사용자가 그리드를 닫아도
        // UIManager Pause 가 남아 timeScale=0 이 고착된다.
        await UniTask.WaitUntil(
            () => UI_GridPanel.Instance == null || !UI_GridPanel.Instance.IsOpen,
            cancellationToken: ct);
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

        // 아이콘 배경을 최고 등급 색으로 물들인다 — 방을 깨고 돌아보는 순간 "이번 건 금색이다"가
        // 성립하고, 주우러 가는 3초가 통째로 기대감 구간이 된다(ARPG loot beam과 같은 역할, §C-3-a).
        var maxRarity = RewardPresentation.MaxRarity(_rewards);
        var rarityCol = RewardPresentation.FrameColor(maxRarity);

        var iconBgGO  = new GameObject("IconBG");
        iconBgGO.transform.SetParent(_worldIndicatorGO.transform, false);
        var iconBgImg = iconBgGO.AddComponent<Image>();
        // Common은 기존 무채색 그대로 — 하위가 눈에 띄면 상위가 사건이 되지 않는다.
        iconBgImg.color = maxRarity == ItemRarity.Common
            ? new Color(0.1f, 0.1f, 0.1f, 0.75f)
            : new Color(rarityCol.r * 0.45f, rarityCol.g * 0.45f, rarityCol.b * 0.45f, 0.85f);
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
        canvas.sortingOrder = UISortingOrder.WorldProp;
        // 프로젝트 캔버스 기준 준수 — 1920×1080, Scale With Screen Size, Match 0.5
        // (설정 없이 붙이면 Constant Pixel Size가 기본이라 고해상도에서 프롬프트가 쪼그라들었다)
        var scaler = _promptGO.AddComponent<CanvasScaler>();
        scaler.uiScaleMode         = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight  = 0.5f;
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
        tmp.text      = $"<color={UIPalette.GoldHex}>[F]</color>  보상 수령";
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
