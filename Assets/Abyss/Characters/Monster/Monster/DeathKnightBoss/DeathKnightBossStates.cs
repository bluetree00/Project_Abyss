using UnityEngine;
using UnityEngine.AI;

namespace Abyss.Monster
{
// ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
// Idle
// ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

public class DKIdleState : IMonsterState
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

// ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
// Chase
// ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

public class DKChaseState : IMonsterState
{
    private const float FaceSpeed      = 8f;
    private const float EngageDistance = 2.5f;
    private const float ChaseStopDist  = 1.0f;

    public void Enter(MonsterContext ctx)
    {
        if (ctx.Agent != null)
        {
            ctx.Agent.enabled = true;
            if (!ctx.Agent.isOnNavMesh) ctx.Monster.TrySnapAgentToNavMesh();
            ctx.Agent.updatePosition  = true;
            ctx.Agent.updateRotation  = false;
            if (ctx.Agent.isOnNavMesh)
            {
                ctx.Agent.isStopped        = false;
                ctx.Agent.stoppingDistance = ChaseStopDist;
            }
            ctx.Agent.speed = ctx.Stat.moveSpeed * ctx.Runtime.SpeedMultiplier;
        }

        ApplyAnimSpeed(ctx);
        PlayAnim(ctx, ctx.Animation.chaseStateName);
    }

    public void Update(MonsterContext ctx)
    {
        if (ctx.Runtime.PlayerTarget == null || ctx.Monster.IsPlayerDead())
        {
            ctx.Monster.ChangeState<PatrolState>();
            return;
        }

        if (ctx.Runtime.DistToPlayer <= EngageDistance)
        {
            ctx.Monster.ChangeState<AttackReadyState>();
            return;
        }

        if (ctx.Agent != null && ctx.Agent.isOnNavMesh)
            ctx.Agent.SetDestination(ctx.Runtime.PlayerTarget.position);

        FacePlayer(ctx);
        // Animator 자체 전환(Run→Run)이 루프를 담당.
        // 혹시 다른 애니메이션으로 밀려났을 때만 Run으로 복귀 (안전망)
        GuardRunAnim(ctx);
    }

    public void Exit(MonsterContext ctx)
    {
        if (ctx.Agent != null)
        {
            ctx.Agent.updateRotation = true;
            if (ctx.Agent.isOnNavMesh) ctx.Agent.ResetPath();
        }
    }

    // Run 상태를 강제로 끊지 않는 안전망 — Animator 자체 전환이 주 루프 담당
    private static void GuardRunAnim(MonsterContext ctx)
    {
        if (ctx.Animator == null || string.IsNullOrEmpty(ctx.Animation.chaseStateName)) return;
        if (ctx.Animator.IsInTransition(0)) return;
        int hash = Animator.StringToHash(ctx.Animation.chaseStateName);
        if (!ctx.Animator.HasState(0, hash)) return;
        var info = ctx.Animator.GetCurrentAnimatorStateInfo(0);
        // Run 이 아닌 다른 상태로 밀려난 경우에만 복귀
        if (info.shortNameHash != hash)
            ctx.Animator.CrossFade(ctx.Animation.chaseStateName, 0.1f);
    }

    private static void FacePlayer(MonsterContext ctx)
    {
        if (ctx.Runtime.PlayerTarget == null) return;
        Vector3 dir = ctx.Runtime.PlayerTarget.position - ctx.Transform.position;
        dir.y = 0f;
        if (dir.sqrMagnitude < 0.001f) return;
        ctx.Transform.rotation = Quaternion.Slerp(
            ctx.Transform.rotation, Quaternion.LookRotation(dir), Time.deltaTime * FaceSpeed);
    }

    private static void ApplyAnimSpeed(MonsterContext ctx)
    {
        if (ctx.Animator == null) return;
        var dk = ctx.Monster as DeathKnightBossMonster;
        ctx.Animator.speed = dk?.DKBlackboard.AnimSpeedMult ?? 1f;
    }

    private static void PlayAnim(MonsterContext ctx, string stateName)
    {
        if (ctx.Animator == null || string.IsNullOrEmpty(stateName)) return;
        if (!ctx.Animator.HasState(0, Animator.StringToHash(stateName))) return;
        // CrossFade 대신 Play로 즉시 시작 — 블렌딩 중 멈춤 버그 방지
        ctx.Animator.Play(stateName, 0, 0f);
    }
}

// ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
// AttackReady
// ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

public class DKAttackReadyState : IMonsterState
{
    private const float FaceSpeed     = 5f;
    // HeavySword 최대 사거리(3.5f)와 일치 — 그 이상이면 Chase로 복귀해 재접근
    private const float ChaseBackDist = 3.5f;
    // Chase에서 진입할 때 달리기→전투대기 애니메이션 전환 시간 + 여유
    private const float SettleDelay   = 0.35f;

    public void Enter(MonsterContext ctx)
    {
        if (ctx.Agent != null && ctx.Agent.isOnNavMesh)
        {
            ctx.Agent.isStopped = true;
            ctx.Agent.velocity  = Vector3.zero; // 잔류 속도 즉시 제거 → 미끄러짐 방지
            ctx.Agent.ResetPath();
        }

        // 달리기 애니메이션이 전환 완료될 때까지 패턴 대기
        (ctx.Monster as DeathKnightBossMonster)?.EnsurePatternDelay(SettleDelay);

        PlayAnim(ctx, ctx.Animation.attackReadyStateName);
    }

    public void Update(MonsterContext ctx)
    {
        if (ctx.Runtime.PlayerTarget == null || ctx.Monster.IsPlayerDead())
        {
            ctx.Monster.ChangeState<PatrolState>();
            return;
        }

        if (ctx.Runtime.DistToPlayer > ChaseBackDist)
        {
            ctx.Monster.ChangeState<ChaseState>();
            return;
        }

        FacePlayer(ctx);
    }

    public void Exit(MonsterContext ctx)
    {
        if (ctx.Agent != null && ctx.Agent.isOnNavMesh)
            ctx.Agent.isStopped = false;
    }

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
        var dk = ctx.Monster as DeathKnightBossMonster;
        ctx.Animator.speed = dk?.DKBlackboard.AnimSpeedMult ?? 1f;
        ctx.Animator.CrossFade(stateName, ctx.Animation.crossFadeDuration);
    }
}

// ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
// Attack (안전망 — 패턴이 처리하므로 AttackReady 로 위임)
// ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

public class DKAttackState : IMonsterState
{
    public void Enter(MonsterContext ctx)  => ctx.Monster.ChangeState<AttackReadyState>();
    public void Update(MonsterContext ctx) { }
    public void Exit(MonsterContext ctx)   { }
}

// ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
// GetHit — gethit1/2/3 랜덤 재생
// ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

public class DKGetHitState : GetHitState
{
    private static readonly string[] HitAnims = { "GetHit1", "GetHit2", "GetHit3" };
    private const float StaggerDuration = 0.5f;

    public override void Enter(MonsterContext ctx)
    {
        if (ctx.Agent != null && ctx.Agent.isActiveAndEnabled && ctx.Agent.isOnNavMesh)
        {
            ctx.Agent.isStopped = true;
            ctx.Agent.velocity  = Vector3.zero;
            ctx.Agent.ResetPath();
        }

        ctx.Runtime.StateTimer = StaggerDuration;
        PlayRandomHitAnim(ctx);
    }

    public override void Update(MonsterContext ctx)
    {
        ctx.Runtime.StateTimer -= Time.deltaTime;
        if (ctx.Runtime.StateTimer > 0f) return;

        RestoreAgent(ctx);

        if (ctx.Runtime.PlayerTarget != null && !ctx.Monster.IsPlayerDead())
            ctx.Monster.ChangeState<ChaseState>();
        else
            ctx.Monster.ChangeState<PatrolState>();
    }

    public override void Exit(MonsterContext ctx)
    {
        if (ctx.Agent != null && ctx.Agent.isActiveAndEnabled && ctx.Agent.isOnNavMesh)
            ctx.Agent.isStopped = false;

        (ctx.Monster as DeathKnightBossMonster)?.DKBlackboard.ClearArmorBroken();
    }

    private static void PlayRandomHitAnim(MonsterContext ctx)
    {
        if (ctx.Animator == null) return;
        string anim = HitAnims[Random.Range(0, HitAnims.Length)];
        if (!ctx.Animator.HasState(0, Animator.StringToHash(anim))) return;
        ctx.Animator.speed = 1f;
        ctx.Animator.CrossFade(anim, 0.05f, 0, 0f);
    }
}

// ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
// Die
// ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

public class DKDieState : DieState
{
    public override void Enter(MonsterContext ctx)
    {
        if (ctx.Monster is DeathKnightBossMonster dk)
            dk.UnbindBossHudIfBoundPublic();

        base.Enter(ctx);
    }
}
}
