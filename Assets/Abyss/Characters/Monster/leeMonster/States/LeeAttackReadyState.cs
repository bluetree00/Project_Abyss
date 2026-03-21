using UnityEngine;

/// <summary>
/// 공격 준비 상태.
/// - 이동 정지, attackDelay(1초) 대기 후 Attack 전환.
/// - 대기 중 플레이어가 사정거리 밖으로 나가면 Chase 전환.
/// - 플레이어 사망 시 Patrol 전환.
/// </summary>
public class LeeAttackReadyState : ILeeMonsterState
{
    public void Enter(LeeMonsterContext ctx)
    {
        ctx.Agent.ResetPath();

        // 첫 조우 시 딜레이 없이 즉시 공격, 이후부터 attackDelay 적용
        ctx.Runtime.StateTimer = ctx.Runtime.IsFirstAttack ? 0f : ctx.Stat.attackDelay;
        ctx.Runtime.IsFirstAttack = false;

        PlayAnim(ctx, ctx.Animation.attackReadyStateName);
        FacePlayer(ctx);
    }

    public void Update(LeeMonsterContext ctx)
    {
        // 플레이어 사망
        if (ctx.Runtime.PlayerTarget == null || ctx.Monster.IsPlayerDead())
        {
            ctx.Monster.ChangeState<LeePatrolState>();
            return;
        }

        float dist = Vector3.Distance(ctx.Transform.position, ctx.Runtime.PlayerTarget.position);

        // 플레이어가 사정거리 밖으로 이탈 (1.3배 여유 허용)
        if (dist > ctx.Stat.attackRange * 1.3f)
        {
            ctx.Monster.ChangeState<LeeChaseState>();
            return;
        }

        FacePlayer(ctx);

        ctx.Runtime.StateTimer -= Time.deltaTime;
        if (ctx.Runtime.StateTimer <= 0f)
            ctx.Monster.ChangeState<LeeAttackState>();
    }

    public void Exit(LeeMonsterContext ctx) { }

    // ── 헬퍼 ──────────────────────────────────────────────

    private static void FacePlayer(LeeMonsterContext ctx)
    {
        if (ctx.Runtime.PlayerTarget == null) return;
        Vector3 dir = ctx.Runtime.PlayerTarget.position - ctx.Transform.position;
        dir.y = 0f;
        if (dir.sqrMagnitude < 0.001f) return;

        ctx.Transform.rotation = Quaternion.Slerp(
            ctx.Transform.rotation,
            Quaternion.LookRotation(dir),
            Time.deltaTime * 15f);
    }

    private static void PlayAnim(LeeMonsterContext ctx, string stateName)
    {
        if (ctx.Animator == null || string.IsNullOrEmpty(stateName)) return;
        ctx.Animator.CrossFade(stateName, ctx.Animation.crossFadeDuration);
    }
}
