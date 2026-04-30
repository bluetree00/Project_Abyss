using UnityEngine;
using Cysharp.Threading.Tasks;

namespace Abyss.Monster
{
/// <summary>
/// DragonBoss 메인 클래스.
///
/// ━━ 설정 ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
///  공통 스탯·패턴은 BossConfigSO (Addressables "DragonBossConfig") 에서 로드.
///  드래곤 고유 수치(비행 높이, 걷기/달리기 임계값 등)는 프리팹 [SerializeField] 로 설정.
///
/// ━━ 상태 구성 ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
///  공통: Idle(PatrolState) · WalkChase(ChaseState) ·
///        RunChase(별도 타입) · AttackReady · AttackState(안전망) ·
///        GetHit · Die
///  드래곤 전용: Takeoff · AirChase · Landing
///
/// ━━ 패턴 시스템 ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
///  BossPatternRunner 가 Update() 에서 틱되어 BossConfigSO 의
///  patternEntries 를 평가하고 패턴 SpecialState 를 발동한다.
/// </summary>
public class DragonBossMonster : MonsterBase, IBoss
{
    // ── 드래곤 전용 설정 (Inspector) ──────────────────────
    [Header("Dragon — 애니메이션 상태 이름")]
    [SerializeField] private string _walkChaseStateName  = "WalkChase";
    [SerializeField] private string _runChaseStateName   = "RunChase";
    [SerializeField] private string _takeoffStateName    = "Takeoff";
    [SerializeField] private string _airChaseStateName   = "AirChase";
    [SerializeField] private string _landingStateName    = "Landing";
    [SerializeField] private string _walkLeftStateName  = "WalkLeft";
    [SerializeField] private string _walkRightStateName = "WalkRight";
    [SerializeField] private string _runLeftStateName   = "RunLeft";
    [SerializeField] private string _runRightStateName  = "RunRight";

    [Header("Dragon — 이동")]
    [Tooltip("이 거리 초과 시 RunChase, 이하 시 WalkChase")]
    [SerializeField] private float _walkToRunThreshold = 8f;
    [Tooltip("WalkChase 속도 배율 (moveSpeed 대비)")]
    [SerializeField] private float _walkChaseSpeedMult = 0.5f;
    [Tooltip("추적 중 NavMeshAgent 회전 속도 (낮을수록 천천히 방향 전환)")]
    [SerializeField] private float _chaseAngularSpeed = 35f;
    [Tooltip("방향 전환 애니 재생 중 이동 속도 배율")]
    [SerializeField] private float _turnSpeedMult = 0.4f;

    [Header("Dragon — 비행")]
    [Tooltip("공중 추적 유지 시간 (초)")]
    [SerializeField] private float _airChaseDuration = 8f;
    [Tooltip("비행 시 Y 오프셋 (m)")]
    [SerializeField] private float _airChaseHeight = 4f;
    [Tooltip("비행 이동 속도 배율 (moveSpeed 대비)")]
    [SerializeField] private float _airChaseSpeedMult = 1.4f;

    // ── 읽기 전용 프로퍼티 (상태 클래스에서 접근) ──────────
    public string WalkChaseStateName   => _walkChaseStateName;
    public string RunChaseStateName    => _runChaseStateName;
    public string TakeoffStateName     => _takeoffStateName;
    public string AirChaseStateName    => _airChaseStateName;
    public string LandingStateName     => _landingStateName;
    public string WalkLeftStateName   => _walkLeftStateName;
    public string WalkRightStateName  => _walkRightStateName;
    public string RunLeftStateName    => _runLeftStateName;
    public string RunRightStateName   => _runRightStateName;
    public float  WalkToRunThreshold  => _walkToRunThreshold;
    public float  ChaseAngularSpeed   => _chaseAngularSpeed;
    public float  WalkChaseSpeedMult  => _walkChaseSpeedMult;
    public float  TurnSpeedMult       => _turnSpeedMult;
    public float  AirChaseDuration    => _airChaseDuration;
    public float  AirChaseHeight      => _airChaseHeight;
    public float  AirChaseSpeedMult   => _airChaseSpeedMult;

    // ── IBoss ─────────────────────────────────────────────
    public float HpRatio =>
        (_config != null && _config.stat.maxHp > 0)
            ? (float)_runtime.CurrentHp / _config.stat.maxHp
            : 1f;

    public BossAttackBlackboard Blackboard => _dragonBB;

    // ── 보스 전용 필드 ────────────────────────────────────
    private DragonBossBlackboard  _dragonBB;
    private BossPatternContext    _patternCtx;
    private BossPatternRunner     _runner;

    // ── 외부 접근 ─────────────────────────────────────────
    public DragonBossBlackboard DragonBlackboard => _dragonBB;

    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    // MonsterBase 추상 멤버
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

    protected override string ConfigAddress => "DragonBossConfig";
    protected override string DataAddress   => null;
    protected override bool   UseWorldHPBar => false;

    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    // 상태 등록 및 보스 시스템 조립
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

    protected override void RegisterStates()
    {
        base.RegisterStates();

        // ── 공통 슬롯 오버라이드 ───────────────────────────
        _fsm.RegisterAs<PatrolState>     (new DragonIdleState());
        _fsm.RegisterAs<ChaseState>      (new DragonWalkChaseState());
        _fsm.RegisterAs<AttackReadyState>(new DragonBossAttackReadyState());
        _fsm.RegisterAs<AttackState>     (new DragonBossAttackState());
        _fsm.RegisterAs<GetHitState>     (new DragonGetHitState());
        _fsm.RegisterAs<DieState>        (new DragonDieState());
        _fsm.Register                    (new DragonRunChaseState());

        // ── 보스 시스템 조립 ───────────────────────────────
        var bossConfig = _config as BossConfigSO;
        if (bossConfig == null) return;

        _dragonBB   = new DragonBossBlackboard();
        _patternCtx = new BossPatternContext
        {
            Boss       = this,
            Ctx        = _ctx,
            Blackboard = _dragonBB,
        };

        BuildConditions(bossConfig);
        InitializePatterns(bossConfig);

        _runner = new BossPatternRunner(
            bossConfig,
            _patternCtx,
            isAlive:     () => !_runtime.IsDead && !IsPlayerDead(),
            isInRange:   IsInEngagementRange,
            changeState: ChangeState,
            onExecuted:  p => _dragonBB.LastPatternTag = p.patternTag);
    }

    protected override void OnInitialized() => BindBossHud();

    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    // 매 프레임
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

    protected override void Update()
    {
        base.Update();
        if (_runtime == null || _runtime.IsDead || _dragonBB == null) return;

        _dragonBB.TickCooldowns(Time.deltaTime);

        if (_runner != null)
        {
            if (!_runner.IsPatternActive)
                _dragonBB.NormalModeTimer += Time.deltaTime;
            else
                _dragonBB.NormalModeTimer = 0f;
        }

        _runner?.Tick(Time.deltaTime);
    }

    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    // 풀 재사용 리셋
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

    protected override void OnEnable()
    {
        base.OnEnable();
        _dragonBB?.Reset();
        _runner?.Reset();
        BindBossHud();
    }

    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    // 드래곤 전용 상태 전환 (상태 클래스에서 호출)
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

    public void UnbindBossHudPublic() => UnbindBossHudIfBound();

    public void UnbindBossHudAfterDelay(float delay)
        => UnbindHudDelayedAsync(delay).Forget();

    private async UniTaskVoid UnbindHudDelayedAsync(float delay)
    {
        await UniTask.Delay(System.TimeSpan.FromSeconds(delay),
            cancellationToken: destroyCancellationToken);
        UnbindBossHudIfBound();
    }

    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    // 내부 헬퍼
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

    // 보스룸에서는 플레이어가 존재하면 항상 추적 시작
    public override bool ShouldStartChase(MonsterContext ctx)
    {
        if (ctx.Runtime.PlayerTarget == null) return false;
        return !IsPlayerDead();
    }

    private bool IsInEngagementRange()
        => _runtime?.PlayerTarget != null
           && _runtime.DistToPlayer < _config.detection.chaseGiveUpRange;

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
                built[i] = BuildCondition(entry.conditions[i], config);
            entry.BuiltConditions = built;
        }
    }

    private ICondition BuildCondition(BossConditionKey key, BossConfigSO config)
    {
        return key switch
        {
            BossConditionKey.Phase2               => new HpBelowCondition(config.condPhase2HpThreshold),
            BossConditionKey.Dist_Close           => new MaxRangeCondition(config.condDistClose),
            BossConditionKey.Dist_Far             => new MinRangeCondition(config.condDistFar),
            BossConditionKey.AfterBackstep        => new LastTagCondition("backstep"),
            BossConditionKey.AfterSidestep        => new LastTagCondition("sidestep"),
            BossConditionKey.TimePressure         => new NormalModeTimerCondition(config.condTimePressureSecs),
            BossConditionKey.Dragon_Summon80      => new DragonSummonedAtCondition(DragonSummonPhase.At80),
            BossConditionKey.Dragon_Summon50      => new DragonSummonedAtCondition(DragonSummonPhase.At50),
            BossConditionKey.Dragon_Summon10      => new DragonSummonedAtCondition(DragonSummonPhase.At10),
            // 속성 페이즈: HP 비율 범위로 판정 (Ice 100-70%, Thunder 70-40%, Fire 40-0%)
            BossConditionKey.Dragon_ElementIce     => new DragonElementPhaseCondition(DragonElementPhase.Ice),
            BossConditionKey.Dragon_ElementThunder => new DragonElementPhaseCondition(DragonElementPhase.Thunder),
            BossConditionKey.Dragon_ElementFire    => new DragonElementPhaseCondition(DragonElementPhase.Fire),
            _                                      => new AlwaysTrue(),
        };
    }
}
}
