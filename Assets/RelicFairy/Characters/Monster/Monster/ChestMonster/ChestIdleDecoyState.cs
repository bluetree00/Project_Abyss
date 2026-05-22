using RelicFairy.Monster;
using UnityEngine;

/// <summary>
/// 체스트 몬스터 위장 대기 특수 상태.
/// 플레이어가 activateRange 이내로 접근하면 ChaseState 로 전환해 전투를 개시한다.
/// InvincibleState 포맷: Invincible 제약 — 대기 중 데미지 완전 차단.
/// </summary>
public class ChestIdleDecoyState : InvincibleState<ChestDecoyData>
{
    public ChestIdleDecoyState(ChestDecoyData data) : base(data) { }

    public override void Enter(MonsterContext ctx)
    {
        if (ctx.Agent.isActiveAndEnabled && ctx.Agent.isOnNavMesh)
            ctx.Agent.ResetPath();
        ctx.Animator?.CrossFade(Data.idleStateName, 0.1f);
        Data.SpawnVFX(ctx.Transform);
    }

    public override void Update(MonsterContext ctx)
    {
        if (ctx.Runtime.PlayerTarget == null) return;

        float dist = Vector3.Distance(ctx.Transform.position, ctx.Runtime.PlayerTarget.position);
        if (dist <= Data.activateRange)
            ctx.Monster.ChangeState<ChaseState>();
    }

    public override void Exit(MonsterContext ctx) { }
}
