using UnityEngine;

namespace Abyss.Monster
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
            DragonSummonPhase.At80 => hp <= 0.8f && !bb.HasSummonedAt80,
            DragonSummonPhase.At50 => hp <= 0.5f && !bb.HasSummonedAt50,
            DragonSummonPhase.At10 => hp <= 0.1f && !bb.HasSummonedAt10,
            _                      => false,
        };
    }
}

public enum DragonSummonPhase { At80, At50, At10 }

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
            _savedAngularSpeed         = ctx.Agent.angularSpeed;
            ctx.Agent.speed            = ctx.Stat.moveSpeed * speedMult * ctx.Runtime.SpeedMultiplier;
            ctx.Agent.stoppingDistance = ctx.Monster.GetCombatStopDistance(ctx);
            ctx.Agent.angularSpeed     = dragon != null ? dragon.ChaseAngularSpeed : 35f;
        }
        _lastDest    = Vector3.positiveInfinity;
        _currentAnim = null;
        UpdateDirectionAnim(ctx);
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
        float angle = Vector3.SignedAngle(ctx.Transform.forward, dir, Vector3.up);

        string target;
        if      (angle < -DirThreshold) target = dragon.WalkLeftStateName;
        else if (angle >  DirThreshold) target = dragon.WalkRightStateName;
        else                            target = dragon.WalkChaseStateName;

        if (_currentAnim == target) return;
        _currentAnim = target;
        _lastDest    = Vector3.positiveInfinity;
        PlayAnim(ctx, target);
    }

    private void Move(MonsterContext ctx)
    {
        var dragon = ctx.Monster as DragonBossMonster;
        bool isDirectional = _currentAnim != null
            && _currentAnim != dragon?.WalkChaseStateName;

        float speedMult = dragon != null ? dragon.WalkChaseSpeedMult : 0.5f;
        ctx.Agent.speed = isDirectional && dragon != null
            ? ctx.Stat.moveSpeed * speedMult * ctx.Runtime.SpeedMultiplier * dragon.TurnSpeedMult
            : ctx.Stat.moveSpeed * speedMult * ctx.Runtime.SpeedMultiplier;

        // 항상 플레이어 위치를 목적지로 설정 — angularSpeed(35f)가 천천히 회전을 담당
        Vector3 dest = ctx.Runtime.PlayerTarget.position;

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
            _savedAngularSpeed         = ctx.Agent.angularSpeed;
            ctx.Agent.speed            = ctx.Stat.moveSpeed * ctx.Runtime.SpeedMultiplier;
            ctx.Agent.stoppingDistance = ctx.Monster.GetCombatStopDistance(ctx);
            ctx.Agent.angularSpeed     = dragon != null ? dragon.ChaseAngularSpeed : 35f;
        }
        _lastDest    = Vector3.positiveInfinity;
        _currentAnim = null;
        UpdateDirectionAnim(ctx);
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
        float angle = Vector3.SignedAngle(ctx.Transform.forward, dir, Vector3.up);

        string target;
        if      (angle < -DirThreshold) target = dragon.RunLeftStateName;
        else if (angle >  DirThreshold) target = dragon.RunRightStateName;
        else                            target = dragon.RunChaseStateName;

        if (_currentAnim == target) return;
        _currentAnim = target;
        _lastDest    = Vector3.positiveInfinity;
        PlayAnim(ctx, target);
    }

    private void Move(MonsterContext ctx)
    {
        var dragon = ctx.Monster as DragonBossMonster;
        bool isDirectional = _currentAnim != null
            && _currentAnim != dragon?.RunChaseStateName;

        ctx.Agent.speed = isDirectional && dragon != null
            ? ctx.Stat.moveSpeed * ctx.Runtime.SpeedMultiplier * dragon.TurnSpeedMult
            : ctx.Stat.moveSpeed * ctx.Runtime.SpeedMultiplier;

        Vector3 dest = ctx.Runtime.PlayerTarget.position;

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
    private const float FaceSpeed = 2.5f;

    public void Enter(MonsterContext ctx)
    {
        if (ctx.Agent != null && ctx.Agent.isOnNavMesh) ctx.Agent.ResetPath();
        PlayAnim(ctx, ctx.Animation.attackReadyStateName);
    }

    public void Update(MonsterContext ctx)
    {
        if (ctx.Runtime.PlayerTarget == null || ctx.Monster.IsPlayerDead())
        {
            ctx.Monster.ChangeState<PatrolState>();
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
        FacePlayer(ctx);
    }

    public void Exit(MonsterContext ctx) { }

    private static void FacePlayer(MonsterContext ctx)
    {
        if (ctx.Runtime.PlayerTarget == null) return;
        Vector3 dir = ctx.Runtime.PlayerTarget.position - ctx.Transform.position;
        dir.y = 0f;
        if (dir.sqrMagnitude < 0.001f) return;
        ctx.Transform.rotation = Quaternion.Slerp(
            ctx.Transform.rotation, Quaternion.LookRotation(dir), Time.deltaTime * FaceSpeed);
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
// 공통 상태 — 피격 (GetHit)
// ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

/// <summary>
/// 피격 상태. 경직 후 거리에 따라 WalkChase / RunChase / Idle 로 복귀한다.
/// </summary>
public class DragonGetHitState : GetHitState
{
    public override void Update(MonsterContext ctx)
    {
        ctx.Runtime.StateTimer -= Time.deltaTime;
        if (ctx.Runtime.StateTimer > 0f) return;

        RestoreAgent(ctx);

        if (ctx.Runtime.PlayerTarget == null || ctx.Monster.IsPlayerDead())
        {
            ctx.Monster.ChangeState<PatrolState>();
            return;
        }

        var dragon = ctx.Monster as DragonBossMonster;
        float threshold = dragon != null ? dragon.WalkToRunThreshold : float.MaxValue;

        if (ctx.Runtime.DistToPlayer > ctx.Detection.chaseGiveUpRange)
            ctx.Monster.ChangeState<PatrolState>();
        else if (ctx.Runtime.DistToPlayer > threshold)
            ctx.Monster.ChangeState<DragonRunChaseState>();
        else
            ctx.Monster.ChangeState<ChaseState>();
    }
}

// ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
// 공통 상태 — 죽음 (Die)
// ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

/// <summary>
/// 죽음 상태. 비행 중 사망 시 NavMesh 복구 후 보스 HUD 해제 + 기본 Die 처리.
/// </summary>
public class DragonDieState : DieState
{
    public override void Enter(MonsterContext ctx)
    {
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

}
