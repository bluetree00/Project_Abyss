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
public class LichMonster : MonsterBase, IBoss
{
    // ── 상수 ─────────────────────────────────────────────────
    private const float Phase2HpThreshold = 0.4f;
    private const float Phase2SpeedMult   = 1.3f;
    private const float Phase2AttackMult  = 1.25f;

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
    private bool                   _phase2Transitioning;

    public LichMovementController MovementController => _movementController;

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
            return lich != null && lich.HpRatio <= Phase2HpThreshold && !_bb.IsPhase2;
        }
    }

    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    // 초기화
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

    protected override void OnInitialized()
    {
        var bossConfig = _config as BossConfigSO;
        if (bossConfig == null)
        {
            Debug.LogError("[LichMonster] Config이 BossConfigSO가 아닙니다!", this);
            return;
        }

        _formController = GetComponentInChildren<LichFormController>();
        _formController?.ApplyForm(LichForm.Phase1);

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
    }

    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    // 매 프레임
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

    protected override void Update()
    {
        base.Update();

        if (_lichBB == null) return;

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
        if (!_lichBB.IsPhase2 && !_phase2Transitioning && HpRatio <= Phase2HpThreshold && !IsInSpecialState)
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

        UI_BossBark.Show("봉인이 풀렸다… 이제 진짜 힘을 보여주마!", BossBarkType.PhaseAnnounce);

        _formController?.ApplyForm(LichForm.Phase2);

        if (_config is BossConfigSO bossConfig)
        {
            bossConfig.patternBreakDurationMin = 0.3f;
            bossConfig.patternBreakDurationMax = 0.8f;
        }

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
