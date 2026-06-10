using System;
using UnityEngine;
using Cysharp.Threading.Tasks;

namespace RelicFairy.Monster
{
/// <summary>
/// DragonBoss 메인 클래스.
///
/// ━━ 설정 ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
///  공통 스탯·패턴은 BossConfigSO (Addressables "DragonBossConfig") 에서 로드.
///  드래곤 고유 수치(걷기/달리기 임계값 등)는 프리팹 [SerializeField] 로 설정.
///  공중 이륙/체공/착지는 각 공중 공격 패턴 SO 내부 FullLockState 가 담당.
///
/// ━━ 상태 구성 ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
///  공통: Idle(PatrolState) · WalkChase(ChaseState) ·
///        RunChase(별도 타입) · AttackReady · AttackState(안전망) ·
///        GetHit · Die
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
    [SerializeField] private string _walkLeftStateName  = "WalkLeft";
    [SerializeField] private string _walkRightStateName = "WalkRight";
    [SerializeField] private string _runLeftStateName   = "RunLeft";
    [SerializeField] private string _runRightStateName  = "RunRight";
    [SerializeField] private string _airChaseStateName = "AirChase";
    [SerializeField] private string _airChaseLeftStateName = "AirChaseLeft";
    [SerializeField] private string _airChaseRightStateName = "AirChaseRight";

    [Header("Dragon — 지상 히트박스")]
    [Tooltip("지상 상태 콜라이더 중심 Y (루트 기준). 프리팹 저장 상태와 무관하게 항상 이 값 사용.")]
    [SerializeField] private float _groundHitboxCenterY = 2.5f;
    [Tooltip("지상 상태 콜라이더 높이")]
    [SerializeField] private float _groundHitboxHeight = 5f;

    [Header("Dragon — 공중 히트박스")]
    [Tooltip("공중 상태에서 지상 높이 기준 배율 (수직 확장 — 투사체 피격 범위)")]
    [SerializeField] private float _airborneHitboxHeightMult = 5f;
    [Tooltip("공중 상태에서 콜라이더 중심을 아래로 이동하는 Y 오프셋 (지상 발사체가 닿도록)")]
    [SerializeField] private float _airborneHitboxCenterOffsetY = 8f;
    [Tooltip("공중 상태에서 콜라이더 반경 (넓을수록 맞추기 쉬움)")]
    [SerializeField] private float _airborneHitboxRadius = 3f;

    [Header("Dragon — 이동")]
    [Tooltip("이 거리 초과 시 RunChase, 이하 시 WalkChase")]
    [SerializeField] private float _walkToRunThreshold = 8f;
    [Tooltip("WalkChase 속도 배율 (moveSpeed 대비)")]
    [SerializeField] private float _walkChaseSpeedMult = 0.5f;
    [Tooltip("추적 중 NavMeshAgent 회전 속도 (낮을수록 천천히 방향 전환)")]
    [SerializeField] private float _chaseAngularSpeed = 12f;
    [Tooltip("방향 전환 애니 재생 중 이동 속도 배율")]
    [SerializeField] private float _turnSpeedMult = 0.4f;
    [SerializeField] private float _groundTurnOrbitAngle = 65f;
    [SerializeField] private float _groundTurnFaceAngle = 25f;
    [SerializeField] private float _airChaseHeight = 4f;
    [SerializeField] private float _airChaseSpeedMult = 1.4f;
    [SerializeField] private float _airTurnAngleThreshold = 40f;
    [SerializeField] private float _airTransitionWeightMultiplier = 8f;
    [SerializeField] private float _airOrbitRadius = 9f;
    [SerializeField] private float _airOrbitAngularSpeed = 70f;
    [SerializeField] private float _airOrbitCatchUpSpeedMult = 2.1f;
    [SerializeField] private float _airOrbitRadiusTolerance = 1.2f;
    [SerializeField] private float _airMinOrbitTurnsBeforePattern = 1f;
    [SerializeField] private float _airOrbitRecenterThreshold = 9f;
    [SerializeField] private float _airOrbitCenterMoveSpeedMult = 1.1f;

    // ── 읽기 전용 프로퍼티 (상태 클래스에서 접근) ──────────
    public string WalkChaseStateName   => _walkChaseStateName;
    public string RunChaseStateName    => _runChaseStateName;
    public string WalkLeftStateName   => _walkLeftStateName;
    public string WalkRightStateName  => _walkRightStateName;
    public string RunLeftStateName    => _runLeftStateName;
    public string RunRightStateName   => _runRightStateName;
    public string AirChaseStateName   => _airChaseStateName;
    public string AirChaseLeftStateName => _airChaseLeftStateName;
    public string AirChaseRightStateName => _airChaseRightStateName;
    public float  WalkToRunThreshold  => _walkToRunThreshold;
    public float  ChaseAngularSpeed   => _chaseAngularSpeed;
    public float  WalkChaseSpeedMult  => _walkChaseSpeedMult;
    public float  TurnSpeedMult       => _turnSpeedMult;
    public float  GroundTurnOrbitAngle => _groundTurnOrbitAngle;
    public float  GroundTurnFaceAngle  => _groundTurnFaceAngle;
    public float  AirChaseHeight      => _airChaseHeight;
    public float  AirChaseSpeedMult   => _airChaseSpeedMult;
    public float  AirTurnAngleThreshold => _airTurnAngleThreshold;
    public float  AirOrbitRadius      => _airOrbitRadius;
    public float  AirOrbitAngularSpeed => _airOrbitAngularSpeed;
    public float  AirOrbitCatchUpSpeedMult => _airOrbitCatchUpSpeedMult;
    public float  AirOrbitRadiusTolerance => _airOrbitRadiusTolerance;
    public float  AirMinOrbitTurnsBeforePattern => _airMinOrbitTurnsBeforePattern;
    public float  AirOrbitRecenterThreshold => _airOrbitRecenterThreshold;
    public float  AirOrbitCenterMoveSpeedMult => _airOrbitCenterMoveSpeedMult;

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

    // ── 공중 히트박스 ─────────────────────────────────────
    private CapsuleCollider _capsule;
    private Vector3         _capsuleCenterNormal; // X/Z 중심 보존용
    private float           _capsuleRadiusNormal; // 지상 반경 보존용
    private bool            _airborneHitboxActive;
    private bool            _hitStopActive;

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
            onExecuted:  OnPatternExecuted);

        UpdateTransitionPatternWeights();
    }

    protected override void OnInitialized()
    {
        BindBossHud();
        _capsule = GetComponent<CapsuleCollider>();
        if (_capsule != null)
        {
            _capsuleCenterNormal = _capsule.center; // X/Z만 보존
            _capsuleRadiusNormal = _capsule.radius; // 지상 반경 보존
            // 프리팹 저장 상태와 무관하게 지상 히트박스로 초기화
            _airborneHitboxActive = true;           // 다음 SyncAirborneHitbox 호출 시 else(지상) 분기 강제 진입
        }
        // 초기 상태: Ice 페이즈 (HP 100%) 색상
        DragonBossVisualHelper.ApplyBodyTint(transform,
            DragonBossVisualHelper.GetElementColor(DragonBossBlackboard.DragonElement.Ice));
    }

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

        UpdateTransitionPatternWeights();

        // 공중 상태에서 최소 선회량을 채울 때까지 패턴 발동 잠금
        bool airPatternLocked = _dragonBB.BodyState == BodyState.Airborne
            && _dragonBB.AirOrbitAccumulatedDegrees < _airMinOrbitTurnsBeforePattern * 360f;
        if (!airPatternLocked)
            _runner?.Tick(Time.deltaTime);

        SyncAirborneHitbox();
    }

    private void SyncAirborneHitbox()
    {
        if (_capsule == null || _dragonBB == null) return;
        bool shouldBeAirborne = _dragonBB.BodyState == BodyState.Airborne;
        if (shouldBeAirborne == _airborneHitboxActive) return;

        _airborneHitboxActive = shouldBeAirborne;
        float cx = _capsuleCenterNormal.x;
        float cz = _capsuleCenterNormal.z;
        if (shouldBeAirborne)
        {
            // 공중: 아래로 확장 + 반경 확대 — 지상에서 조준하기 쉽도록
            _capsule.height = _groundHitboxHeight * _airborneHitboxHeightMult;
            _capsule.center = new Vector3(cx, _groundHitboxCenterY - _airborneHitboxCenterOffsetY, cz);
            _capsule.radius = _airborneHitboxRadius;
        }
        else
        {
            // 지상: 명시적 수치로 바디 중앙에 콜라이더 배치, 반경 원래 값으로 복원
            _capsule.height = _groundHitboxHeight;
            _capsule.center = new Vector3(cx, _groundHitboxCenterY, cz);
            _capsule.radius = _capsuleRadiusNormal;
        }
    }

    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    // 풀 재사용 리셋
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

    protected override void OnEnable()
    {
        base.OnEnable();
        _dragonBB?.Reset();
        _runner?.Reset();
        CacheTransitionPatternBaseWeights();
        UpdateTransitionPatternWeights();
        BindBossHud();
        _hitStopActive = false;
        _airborneHitboxActive = true; // 다음 프레임 SyncAirborneHitbox에서 지상 상태로 강제 복원
        // pool 재활성 시 Ice 페이즈 색상으로 리셋
        DragonBossVisualHelper.ApplyBodyTint(transform,
            DragonBossVisualHelper.GetElementColor(DragonBossBlackboard.DragonElement.Ice));
    }

    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    // 드래곤 전용 상태 전환 (상태 클래스에서 호출)
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    // 피격 처리 (쉴드)
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

    public override void TakeDamage(float amount, GameObject instigator,
        float knockbackMultiplier = 1f,
        bool isCrit = false)
    {
        if (_dragonBB != null && instigator != null)
        {
            Vector3 dir = instigator.transform.position - transform.position;
            _dragonBB.SetHitDirection(dir, transform.forward);
        }
        base.TakeDamage(amount, instigator, knockbackMultiplier, isCrit);

        // 지상 피격 시 히트스톱 (poise 파괴 = 강타격, 일반 = 약타격)
        bool isAirborne = _dragonBB != null && _dragonBB.BodyState == BodyState.Airborne;
        if (!isAirborne)
        {
            float dur = (_dragonBB != null && _dragonBB.IsPoiseBroken) ? 0.14f : 0.05f;
            HitStopAsync(dur, destroyCancellationToken).Forget();
        }
    }

    private async UniTaskVoid HitStopAsync(float duration, System.Threading.CancellationToken ct)
    {
        if (_hitStopActive) return;
        _hitStopActive = true;
        float prev = Time.timeScale;
        Time.timeScale = 0.05f;
        try
        {
            await UniTask.Delay(TimeSpan.FromSeconds(duration), DelayType.Realtime, cancellationToken: ct);
        }
        catch (OperationCanceledException) { }
        finally
        {
            Time.timeScale = prev;
            _hitStopActive = false;
        }
    }

    protected override void OnDamageTaken()
    {
        if (_dragonBB == null) return;

        // 공중 상태 또는 쉴드가 이미 파괴된 상태에서는 GetHit 스킵
        if (_dragonBB.BodyState == BodyState.Airborne)
        {
            _suppressGetHitThisHit = true;
            return;
        }

        bool poiseBroke = _dragonBB.ApplyPoiseDamage();
        if (!poiseBroke)
            _suppressGetHitThisHit = true;
    }

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
    {
        if (_runtime?.PlayerTarget == null)
            return false;

        return _runtime.DistToPlayer < _config.detection.chaseGiveUpRange;
    }

    private void OnPatternExecuted(BossPatternSO pattern)
    {
        if (_dragonBB == null || pattern == null)
            return;

        _dragonBB.LastPatternTag = pattern.patternTag;

        if (pattern is DragonTakeoffPatternSO
            || pattern is DragonLandingPatternSO
            || pattern is DragonSummonPatternSO
            || pattern is DragonIceSlamPatternSO)
        {
            _dragonBB.GroundedPatternStreak = 0;
            _dragonBB.AirbornePatternStreak = 0;
        }
        else if (_dragonBB.BodyState == BodyState.Airborne)
        {
            _dragonBB.AirbornePatternStreak++;
            _dragonBB.GroundedPatternStreak = 0;
        }
        else
        {
            _dragonBB.GroundedPatternStreak++;
            _dragonBB.AirbornePatternStreak = 0;
        }

        UpdateTransitionPatternWeights();
    }

    private void InitializePatterns(BossConfigSO config)
    {
        if (config.patternEntries == null) return;
        foreach (var entry in config.patternEntries)
        {
            if (entry?.patterns == null) continue;
            foreach (var p in entry.patterns)
                p?.Initialize(_patternCtx);
        }

        CacheTransitionPatternBaseWeights();
    }

    private void CacheTransitionPatternBaseWeights()
    {
        if (_dragonBB == null || _config is not BossConfigSO bossConfig || bossConfig.patternEntries == null)
            return;

        foreach (var entry in bossConfig.patternEntries)
        {
            if (entry?.patterns == null)
                continue;

            foreach (var pattern in entry.patterns)
            {
                // pattern.weight 는 런타임에 배수가 곱해지므로 오염될 수 있음.
                // BaseWeight (전용 SO 필드, 코드 비수정) 에서 읽어 SO weight 도 즉시 정규화.
                if (pattern is DragonTakeoffPatternSO tp)
                {
                    _dragonBB.TakeoffBaseWeight = Mathf.Max(0.01f, tp.BaseWeight);
                    pattern.weight = _dragonBB.TakeoffBaseWeight;
                }
                else if (pattern is DragonLandingPatternSO lp)
                {
                    _dragonBB.LandingBaseWeight = Mathf.Max(0.01f, lp.BaseWeight);
                    pattern.weight = _dragonBB.LandingBaseWeight;
                }
            }
        }
    }

    private void UpdateTransitionPatternWeights()
    {
        if (_dragonBB == null || _config is not BossConfigSO bossConfig || bossConfig.patternEntries == null)
            return;

        float takeoffMult = _dragonBB.GroundedPatternStreak >= 2
            ? _airTransitionWeightMultiplier
            : 1f;
        float landingMult = _dragonBB.AirbornePatternStreak >= 2
            ? _airTransitionWeightMultiplier
            : 1f;

        foreach (var entry in bossConfig.patternEntries)
        {
            if (entry?.patterns == null)
                continue;

            foreach (var pattern in entry.patterns)
            {
                if (pattern is DragonTakeoffPatternSO)
                    pattern.weight = _dragonBB.TakeoffBaseWeight * takeoffMult;
                else if (pattern is DragonLandingPatternSO)
                    pattern.weight = _dragonBB.LandingBaseWeight * landingMult;
            }
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
            BossConditionKey.Dragon_Summon70      => new DragonSummonedAtCondition(DragonSummonPhase.At70),
            BossConditionKey.Dragon_Summon40      => new DragonSummonedAtCondition(DragonSummonPhase.At40),
            BossConditionKey.Dragon_Summon10      => new DragonSummonedAtCondition(DragonSummonPhase.At10),
            // 속성 페이즈: HP 비율 범위로 판정 (Ice 100-70%, Thunder 70-40%, Fire 40-0%)
            BossConditionKey.Dragon_ElementIce     => new DragonElementPhaseCondition(DragonElementPhase.Ice),
            BossConditionKey.Dragon_ElementThunder => new DragonElementPhaseCondition(DragonElementPhase.Thunder),
            BossConditionKey.Dragon_ElementFire    => new DragonElementPhaseCondition(DragonElementPhase.Fire),
            // 바디 상태: 지상/공중 — 공중·지상 패턴 풀 필터링용
            BossConditionKey.Dragon_Body_Grounded  => new DragonBodyStateCondition(BodyState.Grounded),
            BossConditionKey.Dragon_Body_Airborne  => new DragonBodyStateCondition(BodyState.Airborne),
            _                                      => new AlwaysTrue(),
        };
    }
}
}
