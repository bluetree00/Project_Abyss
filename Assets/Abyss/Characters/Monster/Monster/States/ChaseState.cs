using UnityEngine;


namespace Abyss.Monster
{
/// <summary>
/// 추격 상태.
/// 플레이어를 향해 NavMesh로 이동.
/// - 공격 사정거리 이내 → AttackReady
/// - 추격 포기 거리 초과 또는 플레이어 사망 → Patrol
/// </summary>
public class ChaseState : IMonsterState
{
    public virtual void Enter(MonsterContext ctx)
    {
        ctx.Agent.speed = ctx.Stat.moveSpeed * ctx.Runtime.SpeedMultiplier;
        ctx.Agent.stoppingDistance = ctx.Stat.attackRange * 0.9f;
        PlayAnim(ctx, ctx.Animation.chaseStateName);

    }

    public virtual void Update(MonsterContext ctx)
    {
        if (ctx.Runtime.PlayerTarget == null || ctx.Monster.IsPlayerDead())
        {
            ctx.Monster.ChangeState<PatrolState>();
            return;
        }

        // 공격 사정거리 이내 → 공격 준비
        if (ctx.Monster.ShouldEnterAttackReady(ctx))
        {
            ctx.Monster.ChangeState<AttackReadyState>();
            return;
        }

        // 추격 포기 → 배회 복귀
        if (ctx.Monster.ShouldGiveUpChase(ctx))
        {
            ctx.Monster.ChangeState<PatrolState>();
            return;
        }

        // 추격 이동 + 회전
        ctx.Agent.SetDestination(ctx.Runtime.PlayerTarget.position);
        FaceTarget(ctx);

        // 이동 속도 파라미터 갱신 (선택)
        if (!string.IsNullOrEmpty(ctx.Animation.speedParam) && ctx.Animator != null)
            ctx.Animator.SetFloat(ctx.Animation.speedParam, ctx.Agent.velocity.magnitude);
    }

    public virtual void Exit(MonsterContext ctx)
    {
        ctx.Agent.ResetPath();

        if (!string.IsNullOrEmpty(ctx.Animation.speedParam) && ctx.Animator != null)
            ctx.Animator.SetFloat(ctx.Animation.speedParam, 0f);
    }

    // ── 헬퍼 ──────────────────────────────────────────────

    protected static void FaceTarget(MonsterContext ctx)
    {
        Vector3 dir = ctx.Runtime.PlayerTarget.position - ctx.Transform.position;
        dir.y = 0f;
        if (dir.sqrMagnitude < 0.001f) return;

        Quaternion target = Quaternion.LookRotation(dir);
        ctx.Transform.rotation = Quaternion.Slerp(
            ctx.Transform.rotation, target, Time.deltaTime * 10f);
    }

    protected static void PlayAnim(MonsterContext ctx, string stateName)
    {
        if (ctx.Animator == null || string.IsNullOrEmpty(stateName)) return;
        ctx.Animator.CrossFade(stateName, ctx.Animation.crossFadeDuration);
    }
}
}
