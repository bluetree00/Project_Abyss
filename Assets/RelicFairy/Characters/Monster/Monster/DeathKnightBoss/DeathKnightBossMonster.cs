using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace RelicFairy.Monster
{
/// <summary>
/// DeathKnight 보스 MonoBehaviour.
///
/// ━━ 페이즈 ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
///  1페이즈 (HP 100~40%): 기본 속도, 패턴 딜레이 2~4s
///  2페이즈 (HP 40%~0%) : 이동속도 1.2x, 패턴 딜레이 1~2.5s
///
/// ━━ 격노(Enrage) ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
///  HP 30% 이하 도달 시 1회 발동.
///  이동속도 1.3x, 애니메이션 속도 1.4x. 해제 없음.
/// </summary>
public class DeathKnightBossMonster : MonsterBase, IBoss
{
    // ── 상수 ─────────────────────────────────────────────
    private const float Phase2SpeedMult    = 1.2f;
    private const float Phase2BreakMin     = 1.0f;
    private const float Phase2BreakMax     = 2.5f;
    private const float EnrageSpeedMult    = 1.3f;
    private const float Phase2HpThreshold  = 0.4f;

    // ── Inspector ─────────────────────────────────────────
    [Header("DeathKnight — 렌더러")]
    [SerializeField] private Renderer[] _bodyRenderers;

    [Header("DeathKnight — 검")]
    [SerializeField] private DeathKnightSwordController _swordCtrl;

    [Header("DeathKnight — 콤보 공격 풀")]
    [SerializeField] private List<BossPatternSO> _attackPool;

    // ── MonsterBase 추상 멤버 ─────────────────────────────
    protected override string ConfigAddress   => "DeathKnightBoss/DeathKnightBossConfig";
    protected override string DataAddress     => string.Empty;
    protected override string HeadBoneName    => null;
    protected override float  HPBarHeadOffset => 0.3f;
    protected override bool   UseWorldHPBar   => false;

    // ── IBoss ─────────────────────────────────────────────
    public float HpRatio =>
        (_runtime != null && _config != null && _config.stat.maxHp > 0)
        ? (float)_runtime.CurrentHp / _config.stat.maxHp
        : 1f;

    public BossAttackBlackboard Blackboard => _coreBB;

    // ── DeathKnight 공개 접근 ─────────────────────────────
    public DeathKnightBossBlackboard DKBlackboard => _dkBB;
    public Transform SwordTransform => _swordCtrl?.SwordTransform;

    // ── 내부 필드 ─────────────────────────────────────────
    private DeathKnightBossBlackboard _dkBB;
    private BossAttackBlackboard      _coreBB;
    private MaterialPropertyBlock     _propBlock;
    private DKComboRunner             _runner;
    private BossPatternContext        _patternCtx;
    private bool                      _prevPatternActive;

    // ── 커스텀 ICondition ─────────────────────────────────

    private sealed class DKPhase2Condition : ICondition
    {
        private readonly DeathKnightBossBlackboard _bb;
        public DKPhase2Condition(DeathKnightBossBlackboard bb) => _bb = bb;
        public bool Evaluate(BossPatternContext ctx) => _bb.IsPhase2;
    }

    private sealed class DKEnragedCondition : ICondition
    {
        private readonly DeathKnightBossBlackboard _bb;
        public DKEnragedCondition(DeathKnightBossBlackboard bb) => _bb = bb;
        public bool Evaluate(BossPatternContext ctx) => _bb.IsEnraged;
    }

    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    // 레이어드 FSM 상태 등록
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

    protected override void RegisterStates()
    {
        base.RegisterStates();
        _fsm.RegisterAs<PatrolState>     (new DKIdleState());
        _fsm.RegisterAs<ChaseState>      (new DKChaseState());
        _fsm.RegisterAs<AttackReadyState>(new DKAttackReadyState());
        _fsm.RegisterAs<AttackState>     (new DKAttackState());
        _fsm.RegisterAs<GetHitState>     (new DKGetHitState());
        _fsm.RegisterAs<DieState>        (new DKDieState());
    }

    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    // 초기화
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

    protected override void OnInitialized()
    {
        var bossConfig = _config as BossConfigSO;
        if (bossConfig == null)
        {
            Debug.LogError("[DeathKnightBossMonster] Config이 BossConfigSO가 아닙니다!", this);
            return;
        }

        _dkBB   = new DeathKnightBossBlackboard();
        _coreBB = new BossAttackBlackboard();

        _patternCtx = new BossPatternContext
        {
            Boss       = this,
            Ctx        = _ctx,
            Blackboard = _coreBB,
        };

        if (_bodyRenderers == null || _bodyRenderers.Length == 0)
            _bodyRenderers = GetComponentsInChildren<SkinnedMeshRenderer>(true);

        if (_swordCtrl == null)
            _swordCtrl = GetComponentInChildren<DeathKnightSwordController>(true);

        BuildConditions(bossConfig);
        InitializePatterns(bossConfig);
        InitializeAttackPool();

        _runner = new DKComboRunner(
            bossConfig,
            _patternCtx,
            _attackPool,
            isAlive:     () => _runtime != null && !_runtime.IsDead && !IsPlayerDead(),
            isInRange:   () => _runtime?.PlayerTarget != null,
            changeState: s  => ChangeState(s),
            onExecuted:  OnPatternExecuted);

        BindBossHud();
    }

    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    // 매 프레임
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

    protected override void Update()
    {
        base.Update();

        if (_dkBB == null || _coreBB == null) return;

        float dt = Time.deltaTime;

        _coreBB.TickCooldowns(dt);

        if (_runner != null)
        {
            bool active = _runner.IsPatternActive;

            if (active)
                _coreBB.NormalModeTimer = 0f;
            else
                _coreBB.NormalModeTimer += dt;

            // 패턴 시작 → 검 등장 / 패턴 종료 → 검 소멸
            if (active != _prevPatternActive)
            {
                if (active)
                {
                    // 등장 전 반드시 올바른 색상 머티리얼 세팅 (핑크 방지)
                    if (_dkBB != null) _swordCtrl?.SetSwordColor(_dkBB.SwordColor);
                    _swordCtrl?.ShowSword();
                }
                else
                {
                    _swordCtrl?.HideSword();
                }
                _prevPatternActive = active;
            }
        }

        _runner?.Tick(dt);

        // 페이즈 전환 체크
        if (!_dkBB.IsPhase2 && HpRatio <= Phase2HpThreshold && !IsInSpecialState)
            EnterPhase2();

        // 격노 체크
        if (!_dkBB.IsEnraged && HpRatio <= DeathKnightBossBlackboard.EnrageHpThreshold)
            TryEnrage();
    }

    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    // 풀 재사용
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

    protected override void OnEnable()
    {
        base.OnEnable();
        _runner?.Reset();
        _coreBB?.Reset();
        _dkBB?.Reset();
        _prevPatternActive = false;
        if (_dkBB != null) ApplyArmorTint(_dkBB.SwordColor);
        if (_attackPool != null)
            foreach (var p in _attackPool)
                p?.OnRecycled();
        BindBossHud();
    }

    protected override void OnDisable()
    {
        UnbindBossHudIfBound();
        base.OnDisable();
    }

    public void UnbindBossHudIfBoundPublic() => UnbindBossHudIfBound();

    /// <summary>AttackReady 진입 시 애니메이션 전환이 끝날 때까지 패턴 대기 보장.</summary>
    public void EnsurePatternDelay(float minDuration) => _runner?.EnsureMinBreakCooldown(minDuration);

    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    // 피격 처리 (아머)
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

    protected override void OnDamageTaken()
    {
        if (_dkBB == null) return;

        bool armorBroke = _dkBB.ApplyArmorDamage(isHeavy: false);
        if (!armorBroke)
            _suppressGetHitThisHit = true;  // 아머 미파괴 → 경직 스킵
        // armorBroke=true 이면 _suppressGetHitThisHit=false 유지 → base가 GetHitState 진입
    }

    /// <summary>
    /// 검 색상 논리값만 반전한다.
    /// 실제 머티리얼 교체는 다음 ShowSword() 직전에 이루어지므로 핑크 검이 노출되지 않는다.
    /// </summary>
    public void FlipSwordColor()
    {
        if (_dkBB == null) return;
        _dkBB.FlipSwordColor();
        // 검이 숨겨진 상태일 때는 지금 바로 머티리얼 세팅 (다음 Show 때도 세팅되지만 안전하게)
        if (!_prevPatternActive)
            _swordCtrl?.SetSwordColor(_dkBB.SwordColor);
        ApplyArmorTint(_dkBB.SwordColor);
    }

    private void ApplyArmorTint(DKSwordColor color)
    {
        if (_bodyRenderers == null || _bodyRenderers.Length == 0) return;
        if (_propBlock == null) _propBlock = new MaterialPropertyBlock();

        Color baseTint = color == DKSwordColor.White
            ? new Color(0.9f,  0.9f,  1.0f, 1f)
            : new Color(0.08f, 0.08f, 0.12f, 1f);
        Color emission = color == DKSwordColor.White
            ? new Color(0.2f, 0.25f, 0.55f, 1f)
            : new Color(0.5f,  0.0f,  0.6f, 1f);

        _propBlock.SetColor("_BaseColor",      baseTint);
        _propBlock.SetColor("_EmissionColor",  emission);
        foreach (var r in _bodyRenderers)
            if (r != null) r.SetPropertyBlock(_propBlock);
    }

    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    // 보스룸: 플레이어가 있으면 항상 추적
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

    public override bool ShouldStartChase(MonsterContext ctx)
    {
        if (ctx.Runtime.PlayerTarget == null) return false;
        return !IsPlayerDead();
    }

    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    // 내부 헬퍼
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

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

    private void InitializeAttackPool()
    {
        if (_attackPool == null) return;
        foreach (var p in _attackPool)
            p?.Initialize(_patternCtx);
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
            BossConditionKey.Phase2       => new HpBelowCondition(config.condPhase2HpThreshold),
            BossConditionKey.Dist_Close   => new MaxRangeCondition(config.condDistClose),
            BossConditionKey.Dist_Far     => new MinRangeCondition(config.condDistFar),
            BossConditionKey.TimePressure => new NormalModeTimerCondition(config.condTimePressureSecs),
            BossConditionKey.DK_IsPhase2  => new DKPhase2Condition(_dkBB),
            BossConditionKey.DK_IsEnraged => new DKEnragedCondition(_dkBB),
            _                             => new AlwaysTrue(),
        };
    }

    private void OnPatternExecuted(BossPatternSO pattern)
    {
        _coreBB.LastPatternTag  = pattern.patternTag;
        _coreBB.NormalModeTimer = 0f;
    }

    // ── 페이즈 전환 ────────────────────────────────────────
    private void EnterPhase2()
    {
        _dkBB.SetPhase2();

        if (_runtime != null)
            _runtime.SpeedMultiplier = Phase2SpeedMult;

        if (_config is BossConfigSO bossConfig)
        {
            bossConfig.patternBreakDurationMin = Phase2BreakMin;
            bossConfig.patternBreakDurationMax = Phase2BreakMax;
        }

        Debug.Log($"[DK] Phase2 진입 — HP={HpRatio:F2}", this);
    }

    // ── 격노 ───────────────────────────────────────────────
    private void TryEnrage()
    {
        if (!_dkBB.TrySetEnraged()) return;

        if (_runtime != null)
            _runtime.SpeedMultiplier = EnrageSpeedMult;

        Debug.Log($"[DK] Enrage 발동 — HP={HpRatio:F2}", this);
    }
}
}
