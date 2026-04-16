using System.Collections.Generic;
using UnityEngine;

namespace Abyss.Monster
{
/// <summary>
/// 왕의 정원 파수꾼 (ForestGuardian) 보스 MonoBehaviour.
///
/// ─ 페이즈 ─────────────────────────────────────────────────────
///  Phase1 (HP 100%~50%) → Phase2 (HP 50%~0%)
///  HP ≤ 50% 감지 시 PhaseChangePending = true → forceExecute 엔트리가 FGPhaseTransitionPatternSO 발동
///  Phase2 진입 후 이동속도 1.25x, 애니 속도 1.2x 적용
///
/// ─ 패턴 엔트리 ─────────────────────────────────────────────────
///  Entry 0 (forceExecute) : PhaseChangePending → FGPhaseTransitionPatternSO
///  Entry 1 (Close ≤5m)    : Punch, GroundSlam, GrabThrow
///  Entry 2 (Far  ≥5m)     : SpinKick, Charge, RockThrow, Breath, GroundSlash
///  Entry 3 (fallback)     : SpinKick, Punch  — 조건 없음, 항상 평가
/// </summary>
public class ForestGuardianMonster : MonsterBase, IBoss
{
    public const string PrefabAddress = "ForestGuardian/ForestGuardian";

    protected override string ConfigAddress   => "ForestGuardian/ForestGuardianConfig";
    protected override string DataAddress     => string.Empty;
    protected override string HeadBoneName    => null;
    protected override float  HPBarHeadOffset => 0.3f;
    protected override bool   UseWorldHPBar   => false;

    [Header("페이즈 머티리얼")]
    [SerializeField] private Material[] _phase1Materials;
    [SerializeField] private Material[] _phase2Materials;

    // ── IBoss ─────────────────────────────────────────────────
    public float HpRatio =>
        (_runtime != null && _config != null && _config.stat.maxHp > 0)
        ? (float)_runtime.CurrentHp / _config.stat.maxHp
        : 1f;

    public BossAttackBlackboard Blackboard => _coreBlackboard;

    // ── ForestGuardian 전용 ────────────────────────────────────
    /// <summary>패턴 SO에서 페이즈 판단 및 그로기 접근 시 사용.</summary>
    public ForestGuardianBlackboard FGBlackboard => _fgBlackboard;

    private ForestGuardianBlackboard _fgBlackboard;
    private BossAttackBlackboard     _coreBlackboard;
    private BossPatternRunner        _runner;
    private BossPatternContext       _patternCtx;
    private BossConfigSO             _bossConfig;
    private bool                     _phase2SpeedApplied;
    private float                    _phase1BreakMin;
    private float                    _phase1BreakMax;
    private SkinnedMeshRenderer      _meshRenderer;

    // ── 커스텀 ICondition ────────────────────────────────────────

    private sealed class FGPhaseChangePendingCondition : ICondition
    {
        private readonly ForestGuardianBlackboard _bb;
        public FGPhaseChangePendingCondition(ForestGuardianBlackboard bb) => _bb = bb;
        public bool Evaluate(BossPatternContext ctx) => _bb.PhaseChangePending;
    }

    private sealed class FGIsPhase2Condition : ICondition
    {
        private readonly ForestGuardianBlackboard _bb;
        public FGIsPhase2Condition(ForestGuardianBlackboard bb) => _bb = bb;
        public bool Evaluate(BossPatternContext ctx) => _bb.IsPhase2;
    }

    private sealed class FGIsGroggyCondition : ICondition
    {
        private readonly ForestGuardianBlackboard _bb;
        public FGIsGroggyCondition(ForestGuardianBlackboard bb) => _bb = bb;
        public bool Evaluate(BossPatternContext ctx) => _bb.IsGroggy;
    }

    // ── 초기화 ───────────────────────────────────────────────────

    protected override void OnInitialized()
    {
        var bossConfig = _config as BossConfigSO;
        if (bossConfig == null)
        {
            Debug.LogError("[ForestGuardianMonster] Config이 BossConfigSO가 아닙니다!", this);
            return;
        }

        _bossConfig         = bossConfig;
        _fgBlackboard       = new ForestGuardianBlackboard();
        _coreBlackboard     = new BossAttackBlackboard();
        _phase2SpeedApplied = false;
        _phase1BreakMin     = bossConfig.patternBreakDurationMin;
        _phase1BreakMax     = bossConfig.patternBreakDurationMax;

        _meshRenderer = GetComponentInChildren<SkinnedMeshRenderer>();
        ApplyMaterials(_phase1Materials);

        _patternCtx = new BossPatternContext
        {
            Boss       = this,
            Ctx        = _ctx,
            Blackboard = _coreBlackboard,
        };

        BuildPatternConditions(bossConfig);

        if (bossConfig.patternEntries != null)
        {
            foreach (var entry in bossConfig.patternEntries)
            {
                if (entry?.patterns == null) continue;
                foreach (var p in entry.patterns)
                    p?.Initialize(_patternCtx);
            }
        }

        _runner = new BossPatternRunner(
            bossConfig,
            _patternCtx,
            isAlive:     () => _runtime != null && !_runtime.IsDead && !IsPlayerDead(),
            isInRange:   () => _runtime?.PlayerTarget != null,
            changeState: s  => ChangeState(s),
            onExecuted:  p  =>
            {
                _coreBlackboard.LastPatternTag  = p.patternTag;
                _coreBlackboard.NormalModeTimer = 0f;
            });

        BindBossHud();
    }

    /// <summary>
    /// Config asset의 entry.conditions(BossConditionKey 열거형 리스트)를 읽어
    /// 런타임 ICondition[] 배열로 조립. DragonBossMonster와 동일한 방식.
    ///
    /// 공용 키 (0~5) : Phase2, Dist_Close, Dist_Far, AfterBackstep, AfterSidestep, TimePressure
    /// FG 전용 키    : FG_PhaseChangePending(12), FG_IsPhase2(13), FG_IsGroggy(14)
    /// </summary>
    private void BuildPatternConditions(BossConfigSO cfg)
    {
        if (cfg.patternEntries == null) return;

        foreach (var entry in cfg.patternEntries)
        {
            if (entry == null || entry.conditions == null || entry.conditions.Count == 0)
            {
                entry.BuiltConditions = null;
                continue;
            }

            var built = new List<ICondition>(entry.conditions.Count);
            foreach (var key in entry.conditions)
            {
                var c = BuildCondition(cfg, key);
                if (c != null) built.Add(c);
            }

            entry.BuiltConditions = built.Count > 0 ? built.ToArray() : null;
        }
    }

    private ICondition BuildCondition(BossConfigSO cfg, BossConditionKey key)
    {
        switch (key)
        {
            // ── 공용 ──────────────────────────────────────────────
            case BossConditionKey.Phase2:
                return new HpBelowCondition(cfg.condPhase2HpThreshold);
            case BossConditionKey.Dist_Close:
                return new MaxRangeCondition(cfg.condDistClose);
            case BossConditionKey.Dist_Far:
                return new MinRangeCondition(cfg.condDistFar);
            case BossConditionKey.AfterBackstep:
                return new LastTagCondition("backstep");
            case BossConditionKey.AfterSidestep:
                return new LastTagCondition("sidestep");
            case BossConditionKey.TimePressure:
                return new NormalModeTimerCondition(cfg.condTimePressureSecs);

            // ── ForestGuardian 전용 ──────────────────────────────
            case BossConditionKey.FG_PhaseChangePending:
                return new FGPhaseChangePendingCondition(_fgBlackboard);
            case BossConditionKey.FG_IsPhase2:
                return new FGIsPhase2Condition(_fgBlackboard);
            case BossConditionKey.FG_IsGroggy:
                return new FGIsGroggyCondition(_fgBlackboard);

            default:
                return null;
        }
    }

    // ── 매 프레임 ─────────────────────────────────────────────

    protected override void Update()
    {
        base.Update();
        if (_runner == null) return;

        _runner.Tick(Time.deltaTime);
        _coreBlackboard?.TickCooldowns(Time.deltaTime);
        _fgBlackboard?.TickGroggy(Time.deltaTime);

        // HP ≤ 50% → Phase2 전환 예약
        if (_fgBlackboard != null && !_fgBlackboard.IsPhase2 && !_fgBlackboard.PhaseChangePending)
        {
            if (HpRatio <= 0.5f)
                _fgBlackboard.TryTriggerPhase2();
        }

        // Phase2 진입 후 이동속도·애니 속도 1회 적용
        if (_fgBlackboard != null && _fgBlackboard.IsPhase2 && !_phase2SpeedApplied)
        {
            _phase2SpeedApplied = true;
            ApplyPhase2Multipliers();
        }

        if (_runner.IsPatternActive)
            _coreBlackboard.NormalModeTimer = 0f;
        else
            _coreBlackboard.NormalModeTimer += Time.deltaTime;

        // Phase2 Animator 속도를 1.2x로 유지 (ChaseState 등이 매 전환마다 1.0으로 리셋하므로)
        if (_fgBlackboard != null && _fgBlackboard.IsPhase2
            && _ctx?.Animator != null && !_runner.IsPatternActive)
        {
            if (_ctx.Animator.speed < ForestGuardianBlackboard.Phase2AnimSpeed - 0.01f)
                _ctx.Animator.speed = ForestGuardianBlackboard.Phase2AnimSpeed;
        }
    }

    private void ApplyMaterials(Material[] mats)
    {
        if (_meshRenderer == null || mats == null || mats.Length == 0) return;
        _meshRenderer.sharedMaterials = mats;
    }

    /// <summary>Phase2 진입 시 NavAgent 이동속도·Animator 속도 배율 적용.</summary>
    private void ApplyPhase2Multipliers()
    {
        // SpeedMultiplier 설정 → ChaseState.Enter() 에서 speed = moveSpeed * SpeedMultiplier로 반영됨
        _runtime.SpeedMultiplier = ForestGuardianBlackboard.Phase2SpeedMult;

        if (_ctx.Agent != null && _ctx.Agent.isOnNavMesh)
            _ctx.Agent.speed = _config.stat.moveSpeed * ForestGuardianBlackboard.Phase2SpeedMult;

        if (_ctx.Animator != null)
            _ctx.Animator.speed = ForestGuardianBlackboard.Phase2AnimSpeed;

        // 패턴 브레이크 딜레이를 Phase2 기획값(0.2~0.7초)으로 갱신
        if (_bossConfig != null)
        {
            _bossConfig.patternBreakDurationMin = ForestGuardianBlackboard.Phase2BreakDurationMin;
            _bossConfig.patternBreakDurationMax = ForestGuardianBlackboard.Phase2BreakDurationMax;
        }

        ApplyMaterials(_phase2Materials);
    }

    // ── 풀 재사용 ─────────────────────────────────────────────

    protected override void OnEnable()
    {
        base.OnEnable();
        _runner?.Reset();
        _coreBlackboard?.Reset();
        _fgBlackboard?.Reset();
        _phase2SpeedApplied = false;

        // 속도 초기화 (SpeedMultiplier는 base.OnEnable()→RuntimeReset에서 1f로 이미 리셋)
        if (_ctx?.Agent != null && _config != null)
            _ctx.Agent.speed = _config.stat.moveSpeed;
        if (_ctx?.Animator != null)
            _ctx.Animator.speed = 1f;

        // 패턴 브레이크 딜레이를 Phase1 기본값으로 복원
        if (_bossConfig != null && _phase1BreakMin > 0f)
        {
            _bossConfig.patternBreakDurationMin = _phase1BreakMin;
            _bossConfig.patternBreakDurationMax = _phase1BreakMax;
        }

        ApplyMaterials(_phase1Materials);
        BindBossHud();
    }

    protected override void OnDisable()
    {
        UnbindBossHudIfBound();
        base.OnDisable();
    }
}
}
