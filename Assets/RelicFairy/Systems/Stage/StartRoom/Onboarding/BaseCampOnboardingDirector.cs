using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

/// <summary>
/// BaseCamp 초회 온보딩 강제 시퀀스 오케스트레이터(상태머신).
/// 순서: 유물 → 장비 → 서약 → 게이트. 각 단계 완료를 퀘스트 보고(Relic/Equip/Covenant)로 감지해
/// 다음 구역의 돔 배리어를 제거하고, **단계 진입 시 그 대상을 카메라 연출(둘러보기)로 보여준다**
/// (초회 첫 연출=유물 즉시, 이후 전환=짧은 지연 후). 가이드 화살표·임시 대사 동반.
/// 완료는 PlayerPrefs로 영속 → 2회차부터 전 구역 개방·연출 스킵.
/// </summary>
public sealed class BaseCampOnboardingDirector : MonoBehaviour
{
    private const string SaveKey = "basecamp_onboarding_done_v1";
    private const float DialogueOpenWait = 0.6f;   // 획득 후 대사 팝업이 열릴 때까지 최대 대기(레이스 방지)

    [Serializable]
    private struct Step
    {
        [Tooltip("완료 감지 퀘스트 카테고리 (Relic / Equip / Covenant)")]
        public string questCategory;
        [Tooltip("화살표/연출 대상(제단·픽업 위치)")]
        public Transform target;
        [Tooltip("이 단계 완료 시 제거할 돔 배리어(다음 구역 개방). 마지막 단계는 게이트 돔.")]
        public GameObject unlockBarrier;
        [TextArea, Tooltip("단계 진입 연출 시 출력할 임시 가이드 문구")]
        public string guideline;
    }

    [Header("단계 (유물 → 장비 → 서약 순)")]
    [SerializeField] private Step[] steps;

    [Header("게이트 (서약 완료 후 목표)")]
    [SerializeField] private Transform gateTarget;
    [SerializeField] private string gateGuideline = "준비 완료 — 포탈로 다음 영역에 진입하라.";

    [Header("가이드")]
    [SerializeField] private OnboardingGuideArrow guideArrow;
    [SerializeField, Tooltip("단계 전환 시 획득 대사 닫힘 후 연출까지 지연(초)")]
    private float transitionDelay = 0.8f;

    [Header("연출 카메라 (천천히 넓게 클로즈업 → 빠르게 복귀)")]
    [SerializeField, Tooltip("대상 기준 카메라 배치 오프셋(뒤/위) — 클로즈업 거리")]
    private Vector3 revealViewOffset = new Vector3(0f, 3f, -6f);
    [SerializeField, Tooltip("바라볼 높이(제단 표시 등)")]
    private float revealLookHeight = 1.2f;
    [SerializeField, Tooltip("이동(천천히)")] private float revealMoveDuration = 2.5f;
    [SerializeField, Tooltip("비추는 유지")] private float revealHoldDuration = 1.0f;
    [SerializeField, Tooltip("복귀(빠르게)")] private float revealReturnDuration = 0.8f;

    [Header("디버그")]
    [SerializeField, Tooltip("켜면 완료 기록 무시하고 항상 초회로 동작")]
    private bool forceFirstRun;

    private int _stepIndex = -1;
    private bool _active;
    private bool _entered;
    private CancellationTokenSource _cts;

    public static bool IsCompleted => PlayerPrefs.GetInt(SaveKey, 0) == 1;

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

        EnterStep(0, initial: true);   // 준비 완료 후 유물 연출
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
        guideArrow?.SetTarget(steps[i].target);
        RevealAsync(steps[i].target, steps[i].guideline, waitDialogue: !initial, _cts.Token).Forget();
    }

    /// <summary>획득 대사가 닫힌 뒤(전환), 현재 카메라 위치에서 대상으로 이동→비추기→플레이어 복귀(orbit 아님). 연출 중 입력 잠금.</summary>
    private async UniTaskVoid RevealAsync(Transform target, string guideline, bool waitDialogue, CancellationToken ct)
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

        var cam = GameCameraController.Instance;
        if (cam == null || target == null) return;

        var pt = Managers.Player?.PlayerTransform;
        var player = pt != null ? pt.GetComponent<PlayerController>() : null;
        player?.SetInputEnabled(false);

        try
        {
            await cam.PlayOnboardingRevealAsync(
                target.position, revealViewOffset, revealLookHeight,
                revealMoveDuration, revealHoldDuration, revealReturnDuration,
                pt, ct);   // 천천히 넓게 클로즈업 → 플레이어 현재 위치로 빠르게 복귀
        }
        catch (OperationCanceledException) { player?.SetInputEnabled(true); return; }

        player?.SetInputEnabled(true);
    }

    private void EnsureBarriersLocked()
    {
        for (int i = 0; i < steps.Length; i++)
            if (steps[i].unlockBarrier != null) steps[i].unlockBarrier.SetActive(true);
    }

    private void UnlockAll()
    {
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
        PlayerPrefs.SetInt(SaveKey, 1);
        PlayerPrefs.Save();
    }

    // ── Event Handlers ────────────────────────────────────────

    private void HandleReport(string category, object target, int amount)
    {
        if (!_active || _stepIndex < 0 || _stepIndex >= steps.Length) return;
        if (!string.Equals(category, steps[_stepIndex].questCategory)) return;

        Unlock(steps[_stepIndex].unlockBarrier);   // 현재 단계 완료 → 다음 구역 개방

        int next = _stepIndex + 1;
        if (next < steps.Length)
        {
            EnterStep(next, initial: false);        // 다음 대상 연출(지연 후)
        }
        else
        {
            _stepIndex = steps.Length;              // 전 단계 완료
            guideArrow?.SetTarget(gateTarget);
            RevealAsync(gateTarget, gateGuideline, waitDialogue: true, _cts.Token).Forget();
            MarkCompleted();
        }
    }
}
