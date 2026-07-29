using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

/// <summary>
/// BaseCamp 초회 온보딩 강제 시퀀스 오케스트레이터(상태머신).
///
/// 순서: <b>무형검 각성 → steps(장비 → 유물) → 게이트</b>.
///  · 무형검 각성은 steps보다 먼저 도는 별도 인트로 단계(swordPhase)다.
///  · steps 순서는 공간 배치(대장간 x89 → 유물 x103)에 맞춰 <b>장비 → 유물</b>이다.
///    ⚠️ 퀘스트 DB 체인(QuestManager)은 이와 별개 시스템이므로, 순서를 바꾸면 양쪽을 함께 맞춰야 한다.
///  · 대상(target)이 없는 단계는 BaseCamp에 실물이 없는 것으로 보고 구역만 열고 자동 통과한다.
///    (서약은 BaseCamp에서 폐기 — 심연 대기방 제단/조립으로 이동.)
///
/// 각 단계 완료를 퀘스트 보고 카테고리(SwordAwaken/Equip/Relic)로 감지해 다음 구역 배리어를 제거하고,
/// 단계 진입 시 그 대상을 카메라 연출(RevealAsync → PlayOnboardingRevealAsync)로 보여준다.
/// 연출 시점은 <b>현재 카메라 heading 기준</b>으로 오프셋이 회전하므로 접근 방향에 맞춰 자연스럽게 잡힌다.
/// 가이드 화살표·가이드 문구 동반. 완료는 PlayerPrefs(슬롯별)로 영속 → 2회차부터 전 구역 개방·연출 스킵.
/// </summary>
public sealed class BaseCampOnboardingDirector : MonoBehaviour
{
    // 완료 기록은 <b>세이브 슬롯별</b>로 보관한다.
    // 전역 키로 두면 슬롯을 지우고 새로 시작해도 온보딩이 다시 나오지 않는다(초회 판정이 깨짐).
    private const string SaveKeyPrefix = "basecamp_onboarding_done_v1_slot";

    private static string SaveKeyFor(int slot) => SaveKeyPrefix + slot;
    private static int    ActiveSlot          => RunProgressManager.Instance?.ActiveSlotIndex ?? 0;
    private const float DialogueOpenWait = 0.6f;   // 획득 후 대사 팝업이 열릴 때까지 최대 대기(레이스 방지)

    [Serializable]
    private struct Step
    {
        [Tooltip("완료 감지 퀘스트 카테고리 (Relic / Equip)")]
        public string questCategory;
        [Tooltip("화살표/연출 대상(제단·픽업 위치)")]
        public Transform target;
        [Tooltip("이 단계 완료 시 제거할 돔 배리어(다음 구역 개방). 마지막 단계는 게이트 돔.")]
        public GameObject unlockBarrier;
        [TextArea, Tooltip("단계 진입 연출 시 출력할 임시 가이드 문구")]
        public string guideline;
    }

    [Header("단계 (유물 → 장비 순. 서약은 BaseCamp에서 폐기 — 심연 대기방으로 이동)")]
    [SerializeField] private Step[] steps;

    [Header("무형검 각성 (인트로 첫 단계 — steps보다 먼저 실행)")]
    [Tooltip("완료 감지 카테고리 (WorldSwordAwakening이 Report하는 값)")]
    [SerializeField] private string     swordAwakenCategory  = "SwordAwaken";
    [Tooltip("각성 제단 — 초회 카메라 연출/화살표 대상. 미할당 시 이 단계 스킵.")]
    [SerializeField] private Transform  swordAwakenTarget;
    [Tooltip("각성 완료 시 해제할 다음 구역 배리어(무기대 등, 선택).")]
    [SerializeField] private GameObject swordAwakenBarrier;
    [TextArea, SerializeField] private string swordAwakenGuideline = "제단의 검을 쥐어라 — [F]";

    [Header("게이트 (장비 완료 후 목표)")]
    [SerializeField] private Transform gateTarget;
    [SerializeField] private string gateGuideline = "준비 완료 — 포탈로 다음 영역에 진입하라.";

    [Header("가이드")]
    [SerializeField] private OnboardingGuideArrow guideArrow;
    [SerializeField, Tooltip("단계 전환 시 획득 대사 닫힘 후 연출까지 지연(초)")]
    private float transitionDelay = 0.8f;

    [Header("디버그")]
    [SerializeField, Tooltip("켜면 완료 기록 무시하고 항상 초회로 동작")]
    private bool forceFirstRun;

    private int _stepIndex = -1;
    private bool _active;
    private bool _entered;
    private bool _swordPhase;
    private CancellationTokenSource _cts;

    /// <summary>현재 활성 슬롯의 온보딩 완료 여부. 초회 판정(입구 스폰 + 가이드) 근거.</summary>
    public static bool IsCompleted => PlayerPrefs.GetInt(SaveKeyFor(ActiveSlot), 0) == 1;

    /// <summary>슬롯 삭제 시 온보딩 기록도 함께 초기화 → 그 슬롯으로 새로 시작하면 초회로 다시 진행된다.</summary>
    public static void ClearForSlot(int slot)
    {
        PlayerPrefs.DeleteKey(SaveKeyFor(slot));
        PlayerPrefs.Save();
    }

    private void Start()
    {
        if (!forceFirstRun && IsCompleted)
        {
            UnlockAll();            // 2회차: 전 구역 개방, 가이드/연출 없음
            guideArrow?.Clear();
            return;
        }

        _active = true;
        _cts = new CancellationTokenSource();
        QuestEvents.OnReported += HandleReport;
        EnsureBarriersLocked();
        // 시작 시엔 구역 잠금만 — 입구 트리거(OnEntered)가 오면 유물부터 연출 시작.
    }

    /// <summary>온보딩 구역 입구 트리거가 호출 — 유물 단계부터 연출 시작(1회).</summary>
    public void OnEntered()
    {
        if (!_active || _entered) return;
        _entered = true;
        BeginAsync(_cts.Token).Forget();
    }

    /// <summary>플레이어 스폰 + 카메라 준비 대기 후 유물 연출 시작 — 인트로 카메라와의 충돌 방지.</summary>
    private async UniTaskVoid BeginAsync(CancellationToken ct)
    {
        try
        {
            await UniTask.WaitUntil(
                () => Managers.Player?.PlayerTransform != null && GameCameraController.Instance != null,
                cancellationToken: ct);
            await UniTask.Delay(TimeSpan.FromSeconds(0.6f), DelayType.UnscaledDeltaTime, cancellationToken: ct);
        }
        catch (OperationCanceledException) { return; }

        // 인트로 첫 단계: 무형검 각성(steps보다 먼저). 대상 없으면 스킵하고 기존 단계부터.
        if (swordAwakenTarget != null) EnterSwordPhase();
        else EnterStep(0, initial: true);
    }

    /// <summary>인트로 첫 단계 — 각성 제단을 초회 카메라 연출로 보여준다. SwordAwaken 보고 시 다음 단계로.</summary>
    private void EnterSwordPhase()
    {
        _swordPhase = true;
        guideArrow?.SetTarget(swordAwakenTarget);
        ShowStepGuideAsync(swordAwakenGuideline, waitDialogue: false, _cts.Token).Forget();
    }

    private void OnDestroy()
    {
        if (_active) QuestEvents.OnReported -= HandleReport;
        _cts?.Cancel();
        _cts?.Dispose();
    }

    // ── 내부 ──────────────────────────────────────────────────

    /// <summary>단계 진입 — 화살표 대상 갱신 + 그 대상을 카메라 연출로 보여준다(전환 시 지연 후).</summary>
    private void EnterStep(int i, bool initial)
    {
        _stepIndex = i;
        if (i < 0 || i >= steps.Length) return;

        // 대상(target)이 없는 단계는 BaseCamp에 실물이 없는 단계다 — 구역만 열고 자동 통과한다.
        // 현재 해당: 서약(획득처가 심연 대기방 WorldCovenantAltar/조립으로 이동함).
        // 퀘스트 체인도 유물 → 장비 → 게이트(tut_enter_gate) → 서약(심연) 순으로 맞춰져 있다. 소프트락 방지.
        if (steps[i].target == null)
        {
            Unlock(steps[i].unlockBarrier);
            AdvanceFrom(i);
            return;
        }

        guideArrow?.SetTarget(steps[i].target);
        ShowStepGuideAsync(steps[i].guideline, waitDialogue: !initial, _cts.Token).Forget();
    }

    /// <summary>단계 i 완료 후 다음으로 — 마지막이면 게이트 연출 + 완료 기록.</summary>
    private void AdvanceFrom(int i)
    {
        int next = i + 1;
        if (next < steps.Length)
        {
            EnterStep(next, initial: false);
        }
        else
        {
            _stepIndex = steps.Length;
            guideArrow?.SetTarget(gateTarget);
            ShowStepGuideAsync(gateGuideline, waitDialogue: true, _cts.Token).Forget();
            MarkCompleted();
        }
    }

    /// <summary>획득 대사가 닫힌 뒤(전환) 다음 단계 가이드 문구를 띄운다. 대상 지시는 가이드 화살표가 담당.</summary>
    private async UniTaskVoid ShowStepGuideAsync(string guideline, bool waitDialogue, CancellationToken ct)
    {
        try
        {
            if (waitDialogue)
            {
                // 획득 → 퀘스트 완료 → 대사 큐(QuestFeedbackPresenter)가 '마지막 대사까지' 끝난 후 연출.
                var feedback = FindFirstObjectByType<QuestFeedbackPresenter>(FindObjectsInactive.Include);
                if (feedback != null)
                {
                    // 대사 큐가 재생을 시작할 때까지 잠깐 대기(Report 직후엔 아직 시작 전 — 레이스 방지)
                    float waited = 0f;
                    while (!feedback.IsPlaying && waited < DialogueOpenWait)
                    {
                        waited += Time.unscaledDeltaTime;
                        await UniTask.Yield(ct);
                    }
                    // 큐의 마지막 대사까지 끝날 때까지 대기
                    await UniTask.WaitUntil(() => feedback == null || !feedback.IsPlaying, cancellationToken: ct);
                }
                // 남은 BlocksGameplay 팝업(선택 UI 등)도 닫힘 대기 — 안전망
                if (Managers.UI != null)
                    await Managers.UI.WaitUntilNoBlockingPopupAsync();
                if (transitionDelay > 0f)
                    await UniTask.Delay(TimeSpan.FromSeconds(transitionDelay), DelayType.UnscaledDeltaTime, cancellationToken: ct);
            }
        }
        catch (OperationCanceledException) { return; }

        ShowGuideline(guideline);   // 임시 대사(자동) — 추후 대사 시퀀스+자동넘김으로 교체

        // 카메라 연출은 걷어냈다. 유물층·무기대는 어차피 한눈에 보이는 공간이라
        // 매번 조작을 뺏고 클로즈업까지 다녀오는 것이 안내가 아니라 방해였다 —
        // 대상 지시는 가이드 화살표(guideArrow)와 위 가이드 문구가 담당한다.
    }

    private void EnsureBarriersLocked()
    {
        if (swordAwakenBarrier != null) swordAwakenBarrier.SetActive(true);
        for (int i = 0; i < steps.Length; i++)
            if (steps[i].unlockBarrier != null) steps[i].unlockBarrier.SetActive(true);
    }

    private void UnlockAll()
    {
        if (swordAwakenBarrier != null) swordAwakenBarrier.SetActive(false);
        for (int i = 0; i < steps.Length; i++)
            if (steps[i].unlockBarrier != null) steps[i].unlockBarrier.SetActive(false);
    }

    private static void Unlock(GameObject barrier)
    {
        if (barrier == null) return;
        if (barrier.TryGetComponent<OnboardingBarrierDome>(out var dome)) dome.Unlock();  // 디졸브 애니메이션
        else barrier.SetActive(false);
    }

    private void ShowGuideline(string text)
    {
        if (string.IsNullOrEmpty(text)) return;
        var hud = FindFirstObjectByType<HudPresenter>(FindObjectsInactive.Include);
        hud?.ShowBuffNotice(text);
        Debug.Log($"[Onboarding] {text}");
    }

    private static void MarkCompleted()
    {
        PlayerPrefs.SetInt(SaveKeyFor(ActiveSlot), 1);
        PlayerPrefs.Save();
    }

    // ── Event Handlers ────────────────────────────────────────

    private void HandleReport(string category, object target, int amount)
    {
        if (!_active) return;

        // 인트로 첫 단계: 무형검 각성 완료 → 다음 구역 개방 후 기존 단계 시작.
        if (_swordPhase)
        {
            if (!string.Equals(category, swordAwakenCategory)) return;
            _swordPhase = false;
            Unlock(swordAwakenBarrier);
            EnterStep(0, initial: false);
            return;
        }

        if (_stepIndex < 0 || _stepIndex >= steps.Length) return;
        if (!string.Equals(category, steps[_stepIndex].questCategory)) return;

        Unlock(steps[_stepIndex].unlockBarrier);   // 현재 단계 완료 → 다음 구역 개방
        AdvanceFrom(_stepIndex);
    }
}
