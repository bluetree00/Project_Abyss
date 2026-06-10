using UnityEngine;

namespace RelicFairy.Monster
{
// ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
// DragonBoss 전용 ICondition 구현체
// ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

internal sealed class DragonSummonedAtCondition : ICondition
{
    private readonly DragonSummonPhase _phase;
    internal DragonSummonedAtCondition(DragonSummonPhase phase) => _phase = phase;

    public bool Evaluate(BossPatternContext ctx)
    {
        if (ctx.Blackboard is not DragonBossBlackboard bb) return false;
        var rt  = ctx.Ctx.Runtime;
        var cfg = ctx.Ctx.Config;
        float hp = cfg?.stat.maxHp > 0 ? (float)rt.CurrentHp / cfg.stat.maxHp : 1f;
        return _phase switch
        {
            DragonSummonPhase.At70 => hp <= 0.7f && !bb.HasSummonedAt70,
            DragonSummonPhase.At40 => hp <= 0.4f && !bb.HasSummonedAt40,
            DragonSummonPhase.At10 => hp <= 0.1f && !bb.HasSummonedAt10,
            _                      => false,
        };
    }
}

public enum DragonSummonPhase { At70, At40, At10 }

// ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
// DragonBoss 속성 페이즈 조건 (HP 비율 범위)
// ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

/// <summary>Ice 100~70% / Thunder 70~40% / Fire 40~0% 속성 페이즈 판정.</summary>
internal sealed class DragonElementPhaseCondition : ICondition
{
    private readonly DragonElementPhase _phase;
    internal DragonElementPhaseCondition(DragonElementPhase phase) => _phase = phase;

    public bool Evaluate(BossPatternContext ctx)
    {
        var cfg = ctx.Ctx.Config;
        var rt  = ctx.Ctx.Runtime;
        if (cfg?.stat == null || cfg.stat.maxHp == 0) return false;

        float ratio = (float)rt.CurrentHp / cfg.stat.maxHp;
        return _phase switch
        {
            DragonElementPhase.Ice     => ratio > 0.7f,
            DragonElementPhase.Thunder => ratio > 0.4f && ratio <= 0.7f,
            DragonElementPhase.Fire    => ratio <= 0.4f,
            _                          => false,
        };
    }
}

internal enum DragonElementPhase { Ice, Thunder, Fire }

// ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
// DragonBoss 바디 상태 조건 (Grounded / Airborne)
// ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

/// <summary>
/// 현재 BodyState 가 지정한 값과 일치하면 참.
/// 공중/지상 패턴 풀 필터링에 사용.
/// </summary>
internal sealed class DragonBodyStateCondition : ICondition
{
    private readonly BodyState _required;
    internal DragonBodyStateCondition(BodyState required) => _required = required;

    public bool Evaluate(BossPatternContext ctx)
    {
        if (ctx.Blackboard is not DragonBossBlackboard bb) return false;
        return bb.BodyState == _required;
    }
}

// ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
// 공통 상태 — 기본(Idle)
// ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

/// <summary>
/// 기본(대기) 상태. PatrolState 슬롯을 대체한다.
/// 이동을 멈추고 Idle 애니메이션을 재생하며, 플레이어 감지 시 거리에 따라
/// WalkChase / RunChase 로 전환한다.
/// </summary>
public class DragonIdleState : IMonsterState
{
    public void Enter(MonsterContext ctx)
    {
        if (ctx.Agent != null && ctx.Agent.isOnNavMesh) ctx.Agent.ResetPath();
        PlayAnim(ctx, ctx.Animation.idleStateName);
    }

    public void Update(MonsterContext ctx)
    {
        if (ctx.Runtime.PlayerTarget == null || ctx.Monster.IsPlayerDead()) return;
        if (!ctx.Monster.ShouldStartChase(ctx)) return;

        var dragon = ctx.Monster as DragonBossMonster;
        float threshold = dragon != null ? dragon.WalkToRunThreshold : float.MaxValue;

        if (ctx.Runtime.DistToPlayer > threshold)
            ctx.Monster.ChangeState<DragonRunChaseState>();
        else
            ctx.Monster.ChangeState<ChaseState>();
    }

    public void Exit(MonsterContext ctx) { }

    private static void PlayAnim(MonsterContext ctx, string stateName)
    {
        if (ctx.Animator == null || string.IsNullOrEmpty(stateName)) return;
        if (!ctx.Animator.HasState(0, Animator.StringToHash(stateName))) return;
        ctx.Animator.speed = 1f;
        ctx.Animator.CrossFade(stateName, ctx.Animation.crossFadeDuration);
    }
}

// ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
// 공통 상태 — 걷기 추적 (WalkChase)
// ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

/// <summary>
/// 걷기 추적 상태. ChaseState 슬롯을 대체한다.
/// 플레이어가 walkToRunThreshold 이내일 때 느린 속도로 추적한다.
/// </summary>
public class DragonWalkChaseState : IMonsterState
{
    private static readonly float DestSqThreshold = 0.09f;
    private const float DirThreshold = 50f;

    private Vector3 _lastDest      = Vector3.positiveInfinity;
    private string  _currentAnim;
    private float   _savedAngularSpeed;

    public void Enter(MonsterContext ctx)
    {
        var dragon = ctx.Monster as DragonBossMonster;
        float speedMult = dragon != null ? dragon.WalkChaseSpeedMult : 0.5f;
        if (ctx.Agent != null)
        {
            EnsureAgentReady(ctx);
            _savedAngularSpeed         = ctx.Agent.angularSpeed;
            ctx.Agent.speed            = ctx.Stat.moveSpeed * speedMult * ctx.Runtime.SpeedMultiplier;
            ctx.Agent.stoppingDistance = ctx.Monster.GetCombatStopDistance(ctx);
            ctx.Agent.angularSpeed     = dragon != null ? dragon.ChaseAngularSpeed : 35f;
        }
        _lastDest    = Vector3.positiveInfinity;
        _currentAnim = null;
        UpdateDirectionAnim(ctx);
    }

    private static void EnsureAgentReady(MonsterContext ctx)
    {
        if (ctx.Agent == null) return;
        if (!ctx.Agent.enabled) ctx.Agent.enabled = true;
        if (ctx.Agent.isOnNavMesh) return;

        bool sampled = UnityEngine.AI.NavMesh.SamplePosition(
            ctx.Transform.position, out var hit, 10f, UnityEngine.AI.NavMesh.AllAreas);
        if (sampled)
        {
            bool warped = ctx.Agent.Warp(hit.position);
            Debug.Log($"[DragonWalkChase] Warp to {hit.position} warped={warped} isOnNavMesh={ctx.Agent.isOnNavMesh}");
        }
        else
        {
            Debug.LogWarning($"[DragonWalkChase] NavMesh.SamplePosition FAILED from {ctx.Transform.position}");
        }
    }

    public void Update(MonsterContext ctx)
    {
        if (ctx.Runtime.PlayerTarget == null || ctx.Monster.IsPlayerDead())
        {
            ctx.Monster.ChangeState<PatrolState>();
            return;
        }
        if (ctx.Monster.ShouldEnterAttackReady(ctx))
        {
            ctx.Monster.ChangeState<AttackReadyState>();
            return;
        }

        var dragon = ctx.Monster as DragonBossMonster;
        float threshold = dragon != null ? dragon.WalkToRunThreshold : float.MaxValue;
        if (ctx.Runtime.DistToPlayer > threshold)
        {
            ctx.Monster.ChangeState<DragonRunChaseState>();
            return;
        }

        UpdateDirectionAnim(ctx);
        Move(ctx);
    }

    public void Exit(MonsterContext ctx)
    {
        _currentAnim = null;
        if (ctx.Agent != null)
        {
            ctx.Agent.angularSpeed = _savedAngularSpeed;
            if (ctx.Agent.isOnNavMesh) ctx.Agent.ResetPath();
        }
    }

    private void UpdateDirectionAnim(MonsterContext ctx)
    {
        if (ctx.Runtime.PlayerTarget == null) return;
        var dragon = ctx.Monster as DragonBossMonster;
        if (dragon == null) return;

        Vector3 dir = ctx.Runtime.PlayerTarget.position - ctx.Transform.position;
        dir.y = 0f;
        float angle    = Vector3.SignedAngle(ctx.Transform.forward, dir, Vector3.up);
        float absAngle = Mathf.Abs(angle);

        bool inTurnAnim = _currentAnim != null && _currentAnim != dragon.WalkChaseStateName;

        string target;
        if (inTurnAnim)
            target = absAngle > dragon.GroundTurnFaceAngle
                ? (angle < 0f ? dragon.WalkLeftStateName : dragon.WalkRightStateName)
                : dragon.WalkChaseStateName;
        else
            target = angle < -DirThreshold ? dragon.WalkLeftStateName
                   : angle >  DirThreshold ? dragon.WalkRightStateName
                   : dragon.WalkChaseStateName;

        if (_currentAnim == target) return;
        _currentAnim = target;
        _lastDest    = Vector3.positiveInfinity;
        PlayAnim(ctx, target);
    }

    private void Move(MonsterContext ctx)
    {
        if (ctx.Agent == null) return;
        if (!ctx.Agent.isOnNavMesh)
        {
            if (UnityEngine.AI.NavMesh.SamplePosition(
                ctx.Transform.position, out var hit, 5f, UnityEngine.AI.NavMesh.AllAreas))
                ctx.Agent.Warp(hit.position);
            if (!ctx.Agent.isOnNavMesh) return;
        }

        var dragon = ctx.Monster as DragonBossMonster;
        bool isDirectional = _currentAnim != null
            && _currentAnim != dragon?.WalkChaseStateName;

        float speedMult = dragon != null ? dragon.WalkChaseSpeedMult : 0.5f;
        ctx.Agent.speed = isDirectional && dragon != null
            ? ctx.Stat.moveSpeed * speedMult * ctx.Runtime.SpeedMultiplier * dragon.TurnSpeedMult
            : ctx.Stat.moveSpeed * speedMult * ctx.Runtime.SpeedMultiplier;

        // 항상 플레이어 위치를 목적지로 설정 — angularSpeed(35f)가 천천히 회전을 담당
        Vector3 dest = isDirectional && dragon != null
            ? DragonGroundTurnUtility.BuildDestination(ctx, dragon)
            : ctx.Runtime.PlayerTarget.position;

        if ((dest - _lastDest).sqrMagnitude > DestSqThreshold)
        {
            ctx.Agent.SetDestination(dest);
            _lastDest = dest;
        }
    }

    private static void PlayAnim(MonsterContext ctx, string stateName)
    {
        if (ctx.Animator == null || string.IsNullOrEmpty(stateName)) return;
        if (!ctx.Animator.HasState(0, Animator.StringToHash(stateName))) return;
        ctx.Animator.speed = 1f;
        ctx.Animator.CrossFade(stateName, ctx.Animation.crossFadeDuration);
    }
}

// ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
// 공통 상태 — 달리기 추적 (RunChase)
// ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

/// <summary>
/// 달리기 추적 상태. walkToRunThreshold 초과 시 빠르게 추적한다.
/// DragonRunChaseState 타입으로 직접 FSM에 등록된다.
/// </summary>
public class DragonRunChaseState : IMonsterState
{
    private static readonly float DestSqThreshold = 0.09f;
    private const float DirThreshold = 50f;

    private Vector3 _lastDest      = Vector3.positiveInfinity;
    private string  _currentAnim;
    private float   _savedAngularSpeed;

    public void Enter(MonsterContext ctx)
    {
        var dragon = ctx.Monster as DragonBossMonster;
        if (ctx.Agent != null)
        {
            EnsureAgentReady(ctx);
            _savedAngularSpeed         = ctx.Agent.angularSpeed;
            ctx.Agent.speed            = ctx.Stat.moveSpeed * ctx.Runtime.SpeedMultiplier;
            ctx.Agent.stoppingDistance = ctx.Monster.GetCombatStopDistance(ctx);
            ctx.Agent.angularSpeed     = dragon != null ? dragon.ChaseAngularSpeed : 35f;
        }
        _lastDest    = Vector3.positiveInfinity;
        _currentAnim = null;
        UpdateDirectionAnim(ctx);
    }

    private static void EnsureAgentReady(MonsterContext ctx)
    {
        if (ctx.Agent == null) return;
        if (!ctx.Agent.enabled) ctx.Agent.enabled = true;
        if (!ctx.Agent.isOnNavMesh
            && UnityEngine.AI.NavMesh.SamplePosition(
                ctx.Transform.position, out var hit, 5f, UnityEngine.AI.NavMesh.AllAreas))
        {
            ctx.Agent.Warp(hit.position);
        }
    }

    public void Update(MonsterContext ctx)
    {
        if (ctx.Runtime.PlayerTarget == null || ctx.Monster.IsPlayerDead())
        {
            ctx.Monster.ChangeState<PatrolState>();
            return;
        }
        if (ctx.Monster.ShouldEnterAttackReady(ctx))
        {
            ctx.Monster.ChangeState<AttackReadyState>();
            return;
        }

        var dragon = ctx.Monster as DragonBossMonster;
        float threshold = dragon != null ? dragon.WalkToRunThreshold : float.MaxValue;
        if (ctx.Runtime.DistToPlayer <= threshold)
        {
            ctx.Monster.ChangeState<ChaseState>();
            return;
        }

        UpdateDirectionAnim(ctx);
        Move(ctx);
    }

    public void Exit(MonsterContext ctx)
    {
        _currentAnim = null;
        if (ctx.Agent != null)
        {
            ctx.Agent.angularSpeed = _savedAngularSpeed;
            if (ctx.Agent.isOnNavMesh) ctx.Agent.ResetPath();
        }
    }

    private void UpdateDirectionAnim(MonsterContext ctx)
    {
        if (ctx.Runtime.PlayerTarget == null) return;
        var dragon = ctx.Monster as DragonBossMonster;
        if (dragon == null) return;

        Vector3 dir = ctx.Runtime.PlayerTarget.position - ctx.Transform.position;
        dir.y = 0f;
        float angle    = Vector3.SignedAngle(ctx.Transform.forward, dir, Vector3.up);
        float absAngle = Mathf.Abs(angle);

        bool inTurnAnim = _currentAnim != null && _currentAnim != dragon.RunChaseStateName;

        string target;
        if (inTurnAnim)
            target = absAngle > dragon.GroundTurnFaceAngle
                ? (angle < 0f ? dragon.RunLeftStateName : dragon.RunRightStateName)
                : dragon.RunChaseStateName;
        else
            target = angle < -DirThreshold ? dragon.RunLeftStateName
                   : angle >  DirThreshold ? dragon.RunRightStateName
                   : dragon.RunChaseStateName;

        if (_currentAnim == target) return;
        _currentAnim = target;
        _lastDest    = Vector3.positiveInfinity;
        PlayAnim(ctx, target);
    }

    private void Move(MonsterContext ctx)
    {
        if (ctx.Agent == null) return;
        if (!ctx.Agent.isOnNavMesh)
        {
            if (UnityEngine.AI.NavMesh.SamplePosition(
                ctx.Transform.position, out var hit, 5f, UnityEngine.AI.NavMesh.AllAreas))
                ctx.Agent.Warp(hit.position);
            if (!ctx.Agent.isOnNavMesh) return;
        }

        var dragon = ctx.Monster as DragonBossMonster;
        bool isDirectional = _currentAnim != null
            && _currentAnim != dragon?.RunChaseStateName;

        ctx.Agent.speed = isDirectional && dragon != null
            ? ctx.Stat.moveSpeed * ctx.Runtime.SpeedMultiplier * dragon.TurnSpeedMult
            : ctx.Stat.moveSpeed * ctx.Runtime.SpeedMultiplier;

        Vector3 dest = isDirectional && dragon != null
            ? DragonGroundTurnUtility.BuildDestination(ctx, dragon)
            : ctx.Runtime.PlayerTarget.position;

        if ((dest - _lastDest).sqrMagnitude > DestSqThreshold)
        {
            ctx.Agent.SetDestination(dest);
            _lastDest = dest;
        }
    }

    private static void PlayAnim(MonsterContext ctx, string stateName)
    {
        if (ctx.Animator == null || string.IsNullOrEmpty(stateName)) return;
        if (!ctx.Animator.HasState(0, Animator.StringToHash(stateName))) return;
        ctx.Animator.speed = 1f;
        ctx.Animator.CrossFade(stateName, ctx.Animation.crossFadeDuration);
    }
}

// ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
// 공통 상태 — 공격 대기 (AttackReady)
// ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

/// <summary>
/// 공격 대기 상태. AttackReadyState 슬롯을 대체한다.
/// 이동을 멈추고 플레이어를 바라보며 BossPatternRunner 의 패턴 발동을 기다린다.
/// </summary>
public class DragonBossAttackReadyState : IMonsterState
{
    private const float ReturnBlendDuration = 1.5f;

    private string _currentAirAnim;
    private int _orbitDirection;
    private float _lastOrbitAngle;
    private bool _hasLastOrbitAngle;
    private Vector3 _orbitCenter;
    private bool _hasOrbitCenter;
    private Vector3 _desiredOrbitCenter;
    private float _returnBlendTimer;
    private bool _topDownActivated;

    public void Enter(MonsterContext ctx)
    {
        var bbRaw = (ctx.Monster as IBoss)?.Blackboard as DragonBossBlackboard;
        Debug.Log($"[DragonAttackReady.Enter] BodyState={bbRaw?.BodyState} GCC={GameCameraController.Instance != null}");

        // 지상 상태 진입 시 Agent 가 이전 공중 패턴으로 disabled 되어 있으면 복구한다.
        if (bbRaw != null && bbRaw.BodyState == BodyState.Grounded)
        {
            Debug.Log("[DragonAttackReady.Enter] → Grounded 분기, DeactivateTopDown");
            GameCameraController.Instance?.DeactivateDragonTopDownView();
            RestoreGroundAgent(ctx);
            if (ctx.Agent != null && ctx.Agent.isOnNavMesh) ctx.Agent.ResetPath();
            PlayAnim(ctx, ctx.Animation.attackReadyStateName);
            return;
        }

        Debug.Log($"[DragonAttackReady.Enter] → Airborne 분기, SpawnPos={ctx.Runtime.SpawnPosition}");
        if (ctx.Agent != null && ctx.Agent.enabled) ctx.Agent.enabled = false;
        _currentAirAnim = null;
        _orbitDirection = ResolveOrbitDirection(ctx);
        _hasOrbitCenter = false;
        _returnBlendTimer = ReturnBlendDuration;
        if (ctx.Runtime.PlayerTarget != null)
        {
            _orbitCenter        = GetPlayerOrbitCenter(ctx, ctx.Runtime.PlayerTarget.position);
            _desiredOrbitCenter = _orbitCenter;
            _hasOrbitCenter     = true;
        }
        _hasLastOrbitAngle = TryGetOrbitAngle(ctx, out _lastOrbitAngle);
        if ((ctx.Monster as IBoss)?.Blackboard is DragonBossBlackboard dragonBb)
            dragonBb.AirOrbitAccumulatedDegrees = 0f;
        _topDownActivated = false;
        // 카메라 전환은 선회궤도 진입 완료 시점(UpdateAirChase)에서 수행
        UpdateAirChaseAnimation(ctx, true);
    }

    public void Update(MonsterContext ctx)
    {
        if (ctx.Runtime.PlayerTarget == null || ctx.Monster.IsPlayerDead())
        {
            ctx.Monster.ChangeState<PatrolState>();
            return;
        }
        if ((ctx.Monster as IBoss)?.Blackboard is DragonBossBlackboard bb
            && bb.BodyState == BodyState.Airborne)
        {
            UpdateAirChase(ctx);
            return;
        }
        if (ctx.Runtime.DistToPlayer > ctx.Monster.GetCombatStopDistance(ctx) * 2.5f)
        {
            var d = ctx.Monster as DragonBossMonster;
            float runThreshold = d != null ? d.WalkToRunThreshold : float.MaxValue;
            if (ctx.Runtime.DistToPlayer > runThreshold)
                ctx.Monster.ChangeState<DragonRunChaseState>();
            else
                ctx.Monster.ChangeState<ChaseState>();
            return;
        }
        FaceTarget(ctx, ctx.Runtime.PlayerTarget.position, 2.5f);
    }

    public void Exit(MonsterContext ctx)
    {
        // 공중 패턴으로 전환 시 카메라는 그대로 유지 — 각 공중 패턴이 Enter()에서 직접 제어
        var bb = (ctx.Monster as IBoss)?.Blackboard as DragonBossBlackboard;
        if (bb == null || bb.BodyState != BodyState.Airborne)
            GameCameraController.Instance?.DeactivateDragonTopDownView();
        _currentAirAnim = null;
        _hasLastOrbitAngle = false;
        _hasOrbitCenter = false;
        _desiredOrbitCenter = default;
        _returnBlendTimer = 0f;
        _topDownActivated = false;
    }

    private void UpdateAirChase(MonsterContext ctx)
    {
        if (ctx.Monster is not DragonBossMonster dragon || ctx.Runtime.PlayerTarget == null)
        {
            if (ctx.Runtime.PlayerTarget != null)
                FaceTarget(ctx, ctx.Runtime.PlayerTarget.position, 2.5f);
            return;
        }

        Vector3 playerPos = ctx.Runtime.PlayerTarget.position;
        RefreshOrbitCenterIfNeeded(ctx, dragon, playerPos);
        OrbitMotion orbit = BuildOrbitMotion(ctx, dragon, _orbitCenter);
        float moveSpeed = ctx.Stat.moveSpeed * dragon.AirChaseSpeedMult;
        float radiusError = Mathf.Abs(GetFlatDistance(ctx.Transform.position, _orbitCenter) - dragon.AirOrbitRadius);
        if (_returnBlendTimer <= 0f && radiusError > dragon.AirOrbitRadiusTolerance)
            moveSpeed *= dragon.AirOrbitCatchUpSpeedMult;

        _returnBlendTimer -= Time.deltaTime;

        // 선회궤도 진입 완료 시점에 탑뷰 카메라 전환 (부자연스러운 이동 구간을 가림)
        if (!_topDownActivated && _returnBlendTimer <= 0f)
        {
            _topDownActivated = true;
            GameCameraController.Instance?.ActivateDragonTopDownView(ctx.Runtime.SpawnPosition);
        }
        UpdateAirChaseAnimation(ctx, false);
        ctx.Transform.position = Vector3.MoveTowards(
            ctx.Transform.position,
            orbit.TargetPosition,
            moveSpeed * Time.deltaTime);
        FaceTarget(ctx, orbit.LookTarget, 5f);

        if ((ctx.Monster as IBoss)?.Blackboard is DragonBossBlackboard dragonBb
            && TryGetOrbitAngle(ctx, out float currentOrbitAngle))
        {
            if (_hasLastOrbitAngle)
                dragonBb.AirOrbitAccumulatedDegrees += Mathf.Abs(Mathf.DeltaAngle(_lastOrbitAngle, currentOrbitAngle));

            _lastOrbitAngle = currentOrbitAngle;
            _hasLastOrbitAngle = true;
        }
    }

    private void UpdateAirChaseAnimation(MonsterContext ctx, bool force)
    {
        if (ctx.Monster is not DragonBossMonster dragon || ctx.Runtime.PlayerTarget == null)
            return;

        string next = _orbitDirection >= 0
            ? dragon.AirChaseRightStateName
            : dragon.AirChaseLeftStateName;

        if (!force && _currentAirAnim == next)
            return;

        _currentAirAnim = next;
        PlayAnim(ctx, next);
    }

    private void RefreshOrbitCenterIfNeeded(MonsterContext ctx, DragonBossMonster dragon, Vector3 playerPos)
    {
        Vector3 playerCenter = GetPlayerOrbitCenter(ctx, playerPos);
        if (!_hasOrbitCenter)
        {
            _orbitCenter = playerCenter;
            _desiredOrbitCenter = playerCenter;
            _hasOrbitCenter = true;
            return;
        }

        float threshold = Mathf.Max(0.1f, dragon.AirOrbitRecenterThreshold);
        if (GetFlatDistance(_orbitCenter, playerCenter) > threshold)
            _desiredOrbitCenter = playerCenter;

        float centerMoveSpeed = ctx.Stat.moveSpeed * Mathf.Max(0.1f, dragon.AirOrbitCenterMoveSpeedMult);
        _orbitCenter = Vector3.MoveTowards(
            _orbitCenter,
            _desiredOrbitCenter,
            centerMoveSpeed * Time.deltaTime);
    }

    private OrbitMotion BuildOrbitMotion(MonsterContext ctx, DragonBossMonster dragon, Vector3 orbitCenter)
    {
        Vector3 offset = ctx.Transform.position - orbitCenter;
        offset.y = 0f;
        if (offset.sqrMagnitude < 0.001f)
            offset = ctx.Transform.right.sqrMagnitude > 0.001f
                ? ctx.Transform.right.normalized * dragon.AirOrbitRadius
                : Vector3.right * dragon.AirOrbitRadius;

        float currentAngle = Mathf.Atan2(offset.z, offset.x);
        float nextAngle = currentAngle + (_orbitDirection * dragon.AirOrbitAngularSpeed * Mathf.Deg2Rad * Time.deltaTime);
        float radius = dragon.AirOrbitRadius;
        Vector3 horizontal = new Vector3(Mathf.Cos(nextAngle), 0f, Mathf.Sin(nextAngle)) * radius;
        Vector3 targetPos = orbitCenter + horizontal;

        Vector3 tangent = _orbitDirection >= 0
            ? new Vector3(-horizontal.z, 0f, horizontal.x)
            : new Vector3(horizontal.z, 0f, -horizontal.x);
        if (tangent.sqrMagnitude < 0.001f)
            tangent = ctx.Transform.forward;
        tangent.Normalize();

        return new OrbitMotion
        {
            TargetPosition = targetPos,
            LookTarget = ctx.Transform.position + tangent * 3f,
        };
    }

    private static Vector3 GetPlayerOrbitCenter(MonsterContext ctx, Vector3 playerPos)
    {
        playerPos.y = ctx.Runtime.SpawnPosition.y + ((ctx.Monster as DragonBossMonster)?.AirChaseHeight ?? 0f);
        return playerPos;
    }

    private static float GetFlatDistance(Vector3 from, Vector3 to)
    {
        from.y = 0f;
        to.y = 0f;
        return Vector3.Distance(from, to);
    }

    private static int ResolveOrbitDirection(MonsterContext ctx)
    {
        if (ctx.Runtime.PlayerTarget == null)
            return 1;

        Vector3 toPlayer = ctx.Runtime.PlayerTarget.position - ctx.Transform.position;
        toPlayer.y = 0f;
        if (toPlayer.sqrMagnitude < 0.001f)
            return 1;

        float sign = Vector3.Cross(ctx.Transform.forward, toPlayer.normalized).y;
        return sign >= 0f ? 1 : -1;
    }

    private bool TryGetOrbitAngle(MonsterContext ctx, out float angleDegrees)
    {
        angleDegrees = 0f;
        if (ctx.Runtime.PlayerTarget == null || ctx.Monster is not DragonBossMonster dragon)
            return false;

        Vector3 center = _hasOrbitCenter
            ? _orbitCenter
            : GetPlayerOrbitCenter(ctx, ctx.Runtime.PlayerTarget.position);
        center.y = ctx.Runtime.SpawnPosition.y + dragon.AirChaseHeight;

        Vector3 offset = ctx.Transform.position - center;
        offset.y = 0f;
        if (offset.sqrMagnitude < 0.001f)
            return false;

        angleDegrees = Mathf.Atan2(offset.z, offset.x) * Mathf.Rad2Deg;
        return true;
    }

    private struct OrbitMotion
    {
        public Vector3 TargetPosition;
        public Vector3 LookTarget;
    }

    private static void RestoreGroundAgent(MonsterContext ctx)
    {
        if (ctx.Agent == null)
            return;

        if (!ctx.Agent.enabled)
            ctx.Agent.enabled = true;

        if (!ctx.Agent.isOnNavMesh
            && UnityEngine.AI.NavMesh.SamplePosition(
                ctx.Transform.position, out var hit, 5f, UnityEngine.AI.NavMesh.AllAreas))
        {
            ctx.Agent.Warp(hit.position);
        }
    }

    private static void FaceTarget(MonsterContext ctx, Vector3 targetPos, float speed)
    {
        Vector3 dir = targetPos - ctx.Transform.position;
        dir.y = 0f;
        if (dir.sqrMagnitude < 0.001f) return;
        ctx.Transform.rotation = Quaternion.Slerp(
            ctx.Transform.rotation, Quaternion.LookRotation(dir), Time.deltaTime * speed);
    }

    private static void PlayAnim(MonsterContext ctx, string stateName)
    {
        if (ctx.Animator == null || string.IsNullOrEmpty(stateName)) return;
        if (!ctx.Animator.HasState(0, Animator.StringToHash(stateName))) return;
        ctx.Animator.speed = 1f;
        ctx.Animator.CrossFade(stateName, ctx.Animation.crossFadeDuration);
    }
}

// ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
// 공통 상태 — 공격 (Attack) 안전망
// ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

/// <summary>
/// 보스 공격 안전망. BossPatternRunner 가 패턴 SpecialState 로 직접 전환하므로
/// 이 상태가 실제로 실행될 일은 없다. 진입 시 즉시 AttackReady 로 복귀한다.
/// </summary>
public class DragonBossAttackState : IMonsterState
{
    public void Enter(MonsterContext ctx)  => ctx.Monster.ChangeState<AttackReadyState>();
    public void Update(MonsterContext ctx) { }
    public void Exit(MonsterContext ctx)   { }
}

// ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
// 공통 상태 — 죽음 (Die)
// ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

// ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
// 공통 상태 — 피격 (GetHit)
// ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

/// <summary>
/// 쉴드 파괴 시에만 진입. 피격 방향에 따라 방향성 GetHit 애니메이션을 재생한다.
/// 공중 상태에서는 DragonBossMonster.OnDamageTaken이 억제하므로 진입하지 않는다.
/// </summary>
public class DragonGetHitState : GetHitState
{
    private const string HitBackAnim  = "UGetHit Back Right";
    private const string HitLeftAnim  = "UGetHit Front Left 1";
    private const string HitRightAnim = "UGetHit Front Right 1";

    public override void Enter(MonsterContext ctx)
    {
        if (ctx.Agent != null && ctx.Agent.isActiveAndEnabled && ctx.Agent.isOnNavMesh)
        {
            ctx.Agent.isStopped = true;
            ctx.Agent.velocity  = Vector3.zero;
            ctx.Agent.ResetPath();
        }

        var dragon = ctx.Monster as DragonBossMonster;
        var bb = dragon?.DragonBlackboard;

        if (bb != null && bb.IsPoiseBroken)
        {
            ctx.Runtime.StateTimer = DragonBossBlackboard.PoiseStaggerTime;
            string anim = GetDirectionalAnim(bb.LastHitDirection);
            if (ctx.Animator != null
                && ctx.Animator.HasState(0, Animator.StringToHash(anim)))
                ctx.Animator.CrossFade(anim, 0.05f, 0, 0f);
        }
        else
        {
            ctx.Runtime.StateTimer = 0f;
        }
    }

    public override void Update(MonsterContext ctx)
    {
        ctx.Runtime.StateTimer -= Time.deltaTime;
        if (ctx.Runtime.StateTimer > 0f) return;

        var dragon = ctx.Monster as DragonBossMonster;
        dragon?.DragonBlackboard?.ClearPoiseBroken();

        RestoreAgent(ctx);

        if (ctx.Runtime.PlayerTarget == null || ctx.Monster.IsPlayerDead())
        {
            ctx.Monster.ChangeState<PatrolState>();
            return;
        }

        float threshold = dragon != null ? dragon.WalkToRunThreshold : float.MaxValue;

        if (ctx.Runtime.DistToPlayer > ctx.Detection.chaseGiveUpRange)
            ctx.Monster.ChangeState<PatrolState>();
        else if (ctx.Runtime.DistToPlayer > threshold)
            ctx.Monster.ChangeState<DragonRunChaseState>();
        else
            ctx.Monster.ChangeState<ChaseState>();
    }

    private static string GetDirectionalAnim(DragonBossBlackboard.HitDirection dir)
    {
        return dir switch
        {
            DragonBossBlackboard.HitDirection.Back => HitBackAnim,
            DragonBossBlackboard.HitDirection.Left => HitLeftAnim,
            _                                      => HitRightAnim,
        };
    }
}

/// <summary>
/// 죽음 상태. 비행 중 사망 시 NavMesh 복구 후 보스 HUD 해제 + 기본 Die 처리.
/// </summary>
public class DragonDieState : DieState
{
    public override void Enter(MonsterContext ctx)
    {
        GameCameraController.Instance?.DeactivateDragonTopDownView();
        if (ctx.Agent != null && !ctx.Agent.enabled)
        {
            ctx.Agent.enabled = true;
            ctx.Agent.Warp(ctx.Monster.transform.position);
        }
        // Despawn 타이밍(3초)에 맞춰 HUD 해제 — 시체가 사라질 때 같이 없어짐
        (ctx.Monster as DragonBossMonster)?.UnbindBossHudAfterDelay(3f);
        base.Enter(ctx);
    }
}

// ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
// 지상 회전 호 이동 헬퍼
// ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

/// <summary>
/// 드래곤이 방향 전환 애니메이션 재생 중 제자리 회전이 아닌 호(arc)를 그리며
/// 이동하도록 NavMeshAgent 목적지를 계산한다.
/// </summary>
internal static class DragonGroundTurnUtility
{
    private const float MinOrbitRadius = 2f;
    private const float MaxOrbitRadius = 8f;
    private const float OrbitDistRatio = 0.5f;

    /// <summary>
    /// 드래곤 위치에서 플레이어 방향으로 <see cref="DragonBossMonster.GroundTurnOrbitAngle"/>만큼
    /// 회전된 방향의 목적지를 반환한다. NavMeshAgent는 이 지점으로 이동하면서 자연스럽게
    /// 호를 그려 플레이어를 향해 돌아선다.
    /// </summary>
    public static Vector3 BuildDestination(MonsterContext ctx, DragonBossMonster dragon)
    {
        if (ctx.Runtime.PlayerTarget == null)
            return ctx.Transform.position;

        Vector3 toPlayer = ctx.Runtime.PlayerTarget.position - ctx.Transform.position;
        toPlayer.y = 0f;

        float distToPlayer = toPlayer.magnitude;
        if (distToPlayer < 0.1f)
            return ctx.Transform.position;

        float signedAngle = Vector3.SignedAngle(ctx.Transform.forward, toPlayer.normalized, Vector3.up);
        float orbitSide   = signedAngle >= 0f ? 1f : -1f;

        Vector3 orbitalDir = Quaternion.Euler(0f, orbitSide * dragon.GroundTurnOrbitAngle, 0f)
                             * ctx.Transform.forward;

        float orbitRadius = Mathf.Clamp(distToPlayer * OrbitDistRatio, MinOrbitRadius, MaxOrbitRadius);
        return ctx.Transform.position + orbitalDir.normalized * orbitRadius;
    }
}

}
