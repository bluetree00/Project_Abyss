using System;
using Cysharp.Threading.Tasks;
using RelicFairy.UI;
using UnityEngine;

namespace RelicFairy.Monster
{
/// <summary>
/// 리치 (Lich) 보스 MonoBehaviour.
/// Chapter 4 최종 보스 — 멀린의 육체를 탈취한 외부 존재.
///
/// ━━ 페이즈 ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
///  Phase 1 (HP 100~40%) : 봉인 상태. 마법 투사체 / 순간이동 타격 패턴.
///  Phase 2 (HP 40~0%)   : 완전 해방. 데스레이 / 영혼 흡수 강화 패턴.
///
/// ━━ 조건 키 ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
///  Lich_Phase1       : HpAboveCondition — 봉인 상태 (HP > 40%)
///  Lich_IsPhase2     : LichBlackboard.IsPhase2 flag — 해방 상태 진입 완료
///  Lich_Phase2Pending: HP ≤ 40% && !IsPhase2 — 페이즈 전환 대기
/// </summary>
public class LichMonster : MonsterBase, IBoss, IBossEntrance
{
    // ── 상수 ─────────────────────────────────────────────────
    private const float Phase2HpThreshold    = 0.4f;
    private const float Phase2SpeedMult      = 1.3f;
    private const float Phase2AttackMult     = 1.25f;
    private const float Phase2HpRestoreRatio = 0.12f; // Phase 2 진입 시 최대 HP의 12% 회복
    private const int   Phase2UnlockAt       = 3;     // 3차+ 조우부터 Phase 2 해금
    private const float RetreatDuration      = 2.5f;
    private const float Phase2PreviewDuration = 12f; // 2차 조우 Phase 2 미리보기 노출 시간 (GDD: 10~20s)

    // ── MonsterBase 추상 멤버 ─────────────────────────────────
    public  const  string PrefabAddress    = "Lich/Lich";
    protected override string ConfigAddress   => "Lich/LichConfig";
    protected override string DataAddress     => string.Empty;
    protected override string HeadBoneName    => null;
    protected override float  HPBarHeadOffset => 0.25f;
    protected override bool   UseWorldHPBar   => false;

    // ── IBoss ─────────────────────────────────────────────────
    public float HpRatio =>
        (_runtime != null && _config != null && _config.stat.maxHp > 0)
        ? (float)_runtime.CurrentHp / _config.stat.maxHp
        : 1f;

    public BossAttackBlackboard Blackboard => _lichBB;

    // ── 공개 접근 ─────────────────────────────────────────────
    public LichBlackboard LichBB => _lichBB;

    // ── 내부 필드 ─────────────────────────────────────────────
    private LichBlackboard         _lichBB;
    private BossPatternRunner      _runner;
    private BossPatternContext     _patternCtx;
    private LichFormController     _formController;
    private LichMovementController _movementController;
    private LichDormantState       _dormantState;
    private bool                   _phase2Transitioning;

    public LichMovementController MovementController => _movementController;

    [Header("── 등장 연출 ──────────────────────────────────")]
    [Tooltip("Appear 애니메이션 종료 후 전투 진입까지 대기 시간 (초). Appear 클립 길이와 맞춘다.")]
    [SerializeField] private float _entranceDuration = 4f;

#if UNITY_EDITOR
    [Header("── 테스트 전용 (빌드 제외) ──────────────────")]
    [SerializeField] private bool _debugOverrideEncounter;
    [Tooltip("시작 조우 횟수. 3 이상이면 Phase 2 해금. 전투가 끝날 때마다 자동으로 +1됨.")]
    [SerializeField] private int  _debugEncounterCount = 1;
    private int _debugSessionCount; // 플레이 중 자동 진행되는 세션 카운터
#endif

    /// <summary>3차+ 조우부터 Phase 2가 영구 해금됨.</summary>
    public bool IsPhase2Unlocked
    {
        get
        {
#if UNITY_EDITOR
            if (_debugOverrideEncounter) return _debugSessionCount >= Phase2UnlockAt;
#endif
            return (BackendGameData.Instance?.Data?.lichEncounterCount ?? 0) >= Phase2UnlockAt;
        }
    }

    // ── 커스텀 ICondition ─────────────────────────────────────

    private sealed class LichPhase2Condition : ICondition
    {
        private readonly LichBlackboard _bb;
        public LichPhase2Condition(LichBlackboard bb) => _bb = bb;
        public bool Evaluate(BossPatternContext ctx) => _bb.IsPhase2;
    }

    private sealed class LichPhase2PendingCondition : ICondition
    {
        private readonly LichBlackboard _bb;
        public LichPhase2PendingCondition(LichBlackboard bb) => _bb = bb;
        public bool Evaluate(BossPatternContext ctx)
        {
            var lich = ctx.Boss as LichMonster;
            return lich != null
                && lich.HpRatio <= Phase2HpThreshold
                && !_bb.IsPhase2;
        }
    }

    // LichMovementController가 이동을 전담하므로 ChaseState 추적 로직은 불필요.
    // 애니메이션만 재생하고 이동은 완전히 LichMovementController에 위임한다.
    private sealed class LichCombatState : IMonsterState
    {
        public void Enter(MonsterContext ctx)
        {
            var a = ctx.Animation;
            if (ctx.Animator != null && !string.IsNullOrEmpty(a.chaseStateName))
                ctx.Animator.CrossFade(a.chaseStateName, a.crossFadeDuration);
        }

        public void Update(MonsterContext ctx) { }
        public void Exit(MonsterContext ctx)   { }
    }

    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    // 초기화
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

    protected override void RegisterStates()
    {
        base.RegisterStates();
        _fsm.RegisterAs<ChaseState>(new LichCombatState());
    }

    protected override void OnInitialized()
    {
        var bossConfig = _config as BossConfigSO;
        if (bossConfig == null)
        {
            Debug.LogError("[LichMonster] Config이 BossConfigSO가 아닙니다!", this);
            return;
        }

        _formController = GetComponentInChildren<LichFormController>();
        _formController?.ApplyForm(LichForm.Phase1); // 의상·후드 표시
        _formController?.HideWeapons();              // 무기는 등장 연출 전까지 숨김

        // 공중 이동 컨트롤러 초기화 (NavMeshAgent 비활성화 후 직접 Transform 제어)
        _movementController = GetComponent<LichMovementController>();
        if (_movementController == null)
            Debug.LogWarning("[LichMonster] LichMovementController 컴포넌트가 없습니다. 프리팹에 추가하세요.", this);

        if (TryGetComponent<UnityEngine.AI.NavMeshAgent>(out var navAgent))
            navAgent.enabled = false;

        _lichBB = new LichBlackboard();

        _patternCtx = new BossPatternContext
        {
            Boss       = this,
            Ctx        = _ctx,
            Blackboard = _lichBB,
        };

        BuildConditions(bossConfig);
        InitializePatterns(bossConfig);

        _runner = new BossPatternRunner(
            bossConfig,
            _patternCtx,
            isAlive:     () => _runtime != null && !_runtime.IsDead && !IsPlayerDead(),
            isInRange:   () => _runtime?.PlayerTarget != null,
            changeState: s  => ChangeState(s),
            onExecuted:  p  => { _lichBB.LastPatternTag = p.patternTag; _lichBB.NormalModeTimer = 0f; });

        _movementController?.Init(_lichBB);

        BindBossHud();

#if UNITY_EDITOR
        if (_debugOverrideEncounter)
        {
            _debugSessionCount = _debugEncounterCount;
            OnDied += Editor_HandleDied;
            Debug.Log($"[LichMonster] 테스트 모드 시작 — {_debugSessionCount}차 조우 (Phase2Unlocked={IsPhase2Unlocked})");
        }
#endif

        // 등장 대기 상태로 진입 — Appear 애니메이션은 TriggerEntrance() 호출 시 시작
        _dormantState = new LichDormantState(_entranceDuration);
        ChangeState(_dormantState);
    }

    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    // 매 프레임
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

    protected override void Update()
    {
        base.Update();

        if (_lichBB == null) return;

        // 등장 연출 중에는 패턴 러너와 무브먼트 완전 정지
        if (_dormantState != null && _dormantState.IsActive) return;

        float dt = Time.deltaTime;

        _lichBB.TickCooldowns(dt);

        if (_runner != null)
        {
            if (_runner.IsPatternActive)
                _lichBB.NormalModeTimer = 0f;
            else
                _lichBB.NormalModeTimer += dt;
        }

        _movementController?.Tick(dt, _runtime?.PlayerTarget);
        _runner?.Tick(dt);

        // Phase2Entry 패턴이 없을 경우 폴백으로 직접 전환
        if (!_lichBB.IsPhase2 && !_phase2Transitioning
            && HpRatio <= Phase2HpThreshold && !IsInSpecialState)
            TriggerPhase2();
    }

    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    // 풀 재사용
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

    protected override void OnEnable()
    {
        base.OnEnable();
        _runner?.Reset();
        _lichBB?.Reset();
        _movementController?.OnRecycled();
        _phase2Transitioning = false;

        // 풀 재사용: Phase1 의상 복원 + 무기 숨기고 등장 대기 재진입
        _formController?.ApplyForm(LichForm.Phase1);
        _formController?.HideWeapons();
        if (_dormantState != null)
            ChangeState(_dormantState);

        BindBossHud();
    }

    protected override void OnDisable()
    {
        UnbindBossHudIfBound();
        base.OnDisable();
    }

    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    // 보스룸 전용: 플레이어가 있으면 항상 추적
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

    public override bool ShouldStartChase(MonsterContext ctx)
    {
        if (ctx.Runtime.PlayerTarget == null) return false;
        return !IsPlayerDead();
    }

    // Lich는 BossPatternRunner 전용 — MonsterBase 기본 근접 공격 완전 비활성화
    public override bool ShouldEnterAttackReady(MonsterContext ctx) => false;

    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    // IBossEntrance
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

    /// <summary>BossSpawner가 카메라 팬 완료 후 호출 — Appear 애니메이션 + 보스 이름 UI 시작.</summary>
    public void TriggerEntrance()
    {
        _dormantState?.TriggerEntrance(_ctx);
    }

    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    // 페이즈 2 진입
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

    /// <summary>LichPhase2EntryPatternSO 완료 후 호출. 속도·공격 속도·패턴 딜레이 적용.</summary>
    public void ApplyPhase2Buffs()
    {
        if (_lichBB == null || _lichBB.IsPhase2) return;

        _lichBB.SetPhase2();

        if (_runtime != null)
            _runtime.SpeedMultiplier = Phase2SpeedMult;

        _lichBB.AttackSpeedMult = Phase2AttackMult;

        // HP 회복 (최대 HP의 12%)
        if (_runtime != null && _config != null)
        {
            int restore = Mathf.RoundToInt(_config.stat.maxHp * Phase2HpRestoreRatio);
            _runtime.CurrentHp = Mathf.Min(_runtime.CurrentHp + restore, _config.stat.maxHp);
        }

        UI_BossBark.Show("영혼이여. 너도 결국 복사될 것이다.", BossBarkType.PhaseAnnounce);

        // Phase2 전환: 책·의복 즉시 숨김, 낫 디졸브 인
        if (_formController != null)
            _formController.DissolveInFormAsync(LichForm.Phase2, destroyCancellationToken).Forget();

        _lichBB.BreakDurationMinOverride = 0.3f;
        _lichBB.BreakDurationMaxOverride = 0.8f;

        Debug.Log($"[Lich] Phase 2 해방 — HP={_runtime?.CurrentHp} ratio={HpRatio:F2}", this);
    }

    private void TriggerPhase2()
    {
        if (_phase2Transitioning) return;
        _phase2Transitioning = true;
        ApplyPhase2Buffs();
    }

    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    // 내부 헬퍼
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

    /// <summary>HP 0 도달 시 호출. 조우 횟수에 따라 퇴각 or 사망.</summary>
    protected override void OnFatalDamage()
    {
        if (!IsPhase2Unlocked)
        {
            // 1·2차 조우: 퇴각 연출 후 보스방 완료
            DoRetreatAsync(destroyCancellationToken).Forget();
            return;
        }
        // 3차+ 조우 완전 격파 — 멀린 내레이션 후 실제 사망 처리
        UI_BossBark.Show("리치가 쓰러졌다. 하지만... 이건 끝이 아니야.", BossBarkType.MerlinNarration);
        base.OnFatalDamage();
    }

    private async UniTaskVoid DoRetreatAsync(System.Threading.CancellationToken ct)
    {
        _movementController?.SetLocked(true);
        _runner?.Reset();

#if UNITY_EDITOR
        int count = _debugOverrideEncounter
            ? _debugSessionCount
            : BackendGameData.Instance?.Data?.lichEncounterCount ?? 1;
#else
        int count = BackendGameData.Instance?.Data?.lichEncounterCount ?? 1;
#endif

        // 2차 조우: Phase 2 형태 잠깐 노출 후 재봉인 (GDD §8.3)
        if (count == 2)
        {
            UI_BossBark.Show("균열이 심화됐다. 다음엔 막지 못할 것이다.", BossBarkType.MerlinNarration);
            if (await DoPhase2PreviewAsync(ct) == false) return;
        }
        else
        {
            string bark = count <= 1 ? "봉인에 균열이 생겼어." : "균열이 심화됐다. 다음엔 막지 못할 것이다.";
            UI_BossBark.Show(bark, BossBarkType.MerlinNarration);
        }

        _ctx.Animator?.CrossFade("Die", 0.2f);

        try
        {
            await UniTask.Delay(TimeSpan.FromSeconds(RetreatDuration), cancellationToken: ct);
        }
        catch (OperationCanceledException)
        {
            return;
        }

#if UNITY_EDITOR
        if (_debugOverrideEncounter)
        {
            Editor_AdvanceAndRestart(ct);
            return;
        }
#endif
        RaiseDied();
        gameObject.SetActive(false);
    }

    /// <summary>
    /// 2차 조우 전용 — Phase 2 비주얼 폼으로 잠깐 전환 후 재봉인.
    /// 취소 시 false 반환.
    /// </summary>
    private async UniTask<bool> DoPhase2PreviewAsync(System.Threading.CancellationToken ct)
    {
        // Phase 2 외형만 전환 (블랙보드·스탯은 건드리지 않음)
        _formController?.ApplyForm(LichForm.Phase2);
        _ctx.Animator?.CrossFade("Phase2Entry", 0.1f);
        UI_BossBark.Show("...다음엔, 이 균열이 봉인을 삼킬 것이다.", BossBarkType.MerlinNarration);

        try
        {
            await UniTask.Delay(TimeSpan.FromSeconds(Phase2PreviewDuration), cancellationToken: ct);
        }
        catch (OperationCanceledException)
        {
            _formController?.ApplyForm(LichForm.Phase1);
            return false;
        }

        // 재봉인 — Phase 1 외형으로 복귀
        _formController?.ApplyForm(LichForm.Phase1);
        _ctx.Animator?.CrossFade("Die", 0.1f);
        return true;
    }

    /// <summary>LichDormantState.TriggerEntrance에서 호출 — Appear 애니메이션 시점에 Phase1 장비 디졸브 인.</summary>
    public void ShowPhase1Form()
    {
        if (_formController == null) return;
        _formController.DissolveInFormAsync(LichForm.Phase1, destroyCancellationToken).Forget();
    }

    /// <summary>LichDormantState.Enter에서 호출 — 플레이어가 실제 보스방에 진입한 시점에 조우 기록.</summary>
    public void StartEncounterRecord() =>
        RecordEncounterAsync(destroyCancellationToken).Forget();

    private async UniTaskVoid RecordEncounterAsync(System.Threading.CancellationToken ct)
    {
#if UNITY_EDITOR
        if (_debugOverrideEncounter)
        {
            Debug.Log($"[LichMonster] 테스트 모드 — 조우 기록 생략 (sessionCount={_debugSessionCount})");
            return;
        }
#endif
        if (BackendGameData.Instance == null) return;
        try
        {
            await BackendGameData.Instance.RecordLichEncounterAsync();
        }
        catch (OperationCanceledException) { }
        catch (Exception e)
        {
            Debug.LogWarning($"[LichMonster] 조우 기록 저장 실패: {e.Message}");
        }
    }

#if UNITY_EDITOR
    private void Editor_HandleDied(MonsterBase _)
    {
        // Phase 2 실제 사망 경로 — DieState에서 RaiseDied() 호출 시 진입
        OnDied -= Editor_HandleDied;
        Editor_DelayedAdvanceAsync(destroyCancellationToken).Forget();
    }

    private async UniTaskVoid Editor_DelayedAdvanceAsync(System.Threading.CancellationToken ct)
    {
        try { await UniTask.Delay(TimeSpan.FromSeconds(3f), cancellationToken: ct); }
        catch (OperationCanceledException) { return; }
        Editor_AdvanceAndRestart(ct);
    }

    private void Editor_AdvanceAndRestart(System.Threading.CancellationToken ct)
    {
        int prev = _debugSessionCount;
        _debugSessionCount++;
        Debug.Log($"[LichMonster] 테스트 — {prev}차 조우 완료 → {_debugSessionCount}차 시작 (Phase2Unlocked={IsPhase2Unlocked})");
        Editor_ResetFight();
    }

    /// <summary>에디터 전용 — 보스 상태를 초기화해 현재 세션 카운터 기준으로 재시작.</summary>
    [ContextMenu("테스트: 전투 리셋 (카운터 유지)")]
    private void Editor_ResetFight()
    {
        if (!Application.isPlaying) return;
        OnDied -= Editor_HandleDied;

        _phase2Transitioning = false;
        _runner?.Reset();
        _lichBB?.Reset();
        _movementController?.OnRecycled();
        if (_runtime != null)
        {
            _runtime.IsDead    = false;
            _runtime.CurrentHp = _config?.stat.maxHp ?? 100;
        }
        _formController?.ApplyForm(LichForm.Phase1);
        gameObject.SetActive(true);
        ChangeState<ChaseState>();

        OnDied += Editor_HandleDied;
        Debug.Log($"[LichMonster] 전투 리셋 — {_debugSessionCount}차 조우 (Phase2Unlocked={IsPhase2Unlocked})");
    }

    /// <summary>에디터 전용 — 세션 카운터를 Inspector 초기값으로 되돌리고 재시작.</summary>
    [ContextMenu("테스트: 세션 카운터 리셋 (1차부터)")]
    private void Editor_ResetSession()
    {
        if (!Application.isPlaying) return;
        _debugSessionCount = _debugEncounterCount;
        Editor_ResetFight();
        Debug.Log($"[LichMonster] 세션 리셋 — {_debugSessionCount}차부터 재시작");
    }
#endif

    private void InitializePatterns(BossConfigSO config)
    {
        if (config.patternEntries == null) return;
        foreach (var entry in config.patternEntries)
        {
            if (entry?.patterns == null) continue;
            foreach (var p in entry.patterns)
                p?.Initialize(_patternCtx);
        }
    }

    private void BuildConditions(BossConfigSO config)
    {
        if (config.patternEntries == null) return;
        foreach (var entry in config.patternEntries)
        {
            if (entry == null) continue;
            if (entry.conditions == null || entry.conditions.Count == 0)
            {
                entry.BuiltConditions = null;
                continue;
            }

            var built = new ICondition[entry.conditions.Count];
            for (int i = 0; i < entry.conditions.Count; i++)
                built[i] = BuildSingleCondition(entry.conditions[i], config);
            entry.BuiltConditions = built;
        }
    }

    private ICondition BuildSingleCondition(BossConditionKey key, BossConfigSO config)
    {
        return key switch
        {
            BossConditionKey.Phase2              => new HpBelowCondition(config.condPhase2HpThreshold),
            BossConditionKey.Dist_Close          => new MaxRangeCondition(config.condDistClose),
            BossConditionKey.Dist_Far            => new MinRangeCondition(config.condDistFar),
            BossConditionKey.TimePressure        => new NormalModeTimerCondition(config.condTimePressureSecs),
            BossConditionKey.Lich_Phase1         => new HpAboveCondition(config.condPhase2HpThreshold),
            BossConditionKey.Lich_IsPhase2       => new LichPhase2Condition(_lichBB),
            BossConditionKey.Lich_Phase2Pending  => new LichPhase2PendingCondition(_lichBB),
            _                                    => new AlwaysTrue(),
        };
    }
}
}
