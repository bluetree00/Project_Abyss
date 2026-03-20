using UnityEngine;

/// <summary>
/// 피격 상태.
/// 짧은 경직 후 상황에 따라 Chase 또는 Patrol 로 복귀.
/// </summary>
public class LeeGetHitState : ILeeMonsterState
{
    private const float StunDuration = 0.4f;

    public void Enter(LeeMonsterContext ctx)
    {
        // NavMeshAgent를 끄고 Rigidbody에게 넉백 물리를 맡긴다.
        // (둘이 동시에 켜져 있으면 위치 제어가 충돌해 Agent 내부 상태가 깨짐)
        ctx.Agent.enabled = false;
        ctx.Runtime.StateTimer = StunDuration;

        if (ctx.Animator != null && !string.IsNullOrEmpty(ctx.Animation.getHitTrigger))
            ctx.Animator.CrossFade(ctx.Animation.getHitTrigger, 0.05f, 0, 0f);
    }

    public void Update(LeeMonsterContext ctx)
    {
        ctx.Runtime.StateTimer -= Time.deltaTime;
        if (ctx.Runtime.StateTimer > 0f) return;

        // 경직 종료 전에 Agent 복구 (다음 상태에서 바로 쓸 수 있도록)
        RestoreAgent(ctx);

        if (ctx.Runtime.PlayerTarget != null && !ctx.Monster.IsPlayerDead())
        {
            float dist = Vector3.Distance(
                ctx.Transform.position, ctx.Runtime.PlayerTarget.position);

            ctx.Monster.ChangeState(
                dist <= ctx.Detection.chaseGiveUpRange
                    ? LeeMonsterStateType.Chase
                    : LeeMonsterStateType.Patrol);
        }
        else
        {
            ctx.Monster.ChangeState(LeeMonsterStateType.Patrol);
        }
    }

    public void Exit(LeeMonsterContext ctx)
    {
        // Update 도중 전환이 아닌 경로(예: Die)로 빠져나올 경우 대비
        RestoreAgent(ctx);
    }

    private static void RestoreAgent(LeeMonsterContext ctx)
    {
        if (ctx.Agent.enabled) return;

        // 넉백으로 밀린 Rigidbody 속도를 정지시킨 후 kinematic 복원 → NavMesh에 재스냅
        var rb = ctx.Monster.GetComponent<Rigidbody>();
        if (rb != null)
        {
            rb.linearVelocity  = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
            rb.isKinematic     = true;
        }

        ctx.Agent.enabled = true;
        ctx.Agent.Warp(ctx.Monster.transform.position);
    }
}
