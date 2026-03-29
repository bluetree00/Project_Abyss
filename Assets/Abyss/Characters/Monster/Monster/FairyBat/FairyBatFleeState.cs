using Abyss.Monster;
using UnityEngine;

/// <summary>
/// 페어리박쥐 도주 특수 상태.
/// HP 임계값 이하 도달 시 1회 발동 — 피격 경직 없이 플레이어 반대 방향으로 도주 후 Patrol 복귀.
/// UnInterruptibleState 포맷: UnInterruptible 제약 자동 바인딩.
/// </summary>
public class FairyBatFleeState : UnInterruptibleState<FairyBatFleeData>
{
    private float _timer;

    public FairyBatFleeState(FairyBatFleeData data) : base(data) { }

    public override void Enter(MonsterContext ctx)
    {
        _timer = Data.fleeDuration;
        ctx.Agent.speed = ctx.Config.stat.moveSpeed * Data.fleeSpeedMult;
        ctx.Animator?.CrossFade("MoveBlend", 0.15f);
    }

    public override void Update(MonsterContext ctx)
    {
        if (ctx.Runtime.PlayerTarget != null)
        {
            Vector3 away = (ctx.Transform.position - ctx.Runtime.PlayerTarget.position).normalized;
            ctx.Agent.SetDestination(ctx.Transform.position + away * 3f);
        }

        _timer -= Time.deltaTime;
        if (_timer <= 0f)
            ctx.Monster.ChangeState<PatrolState>();
    }

    public override void Exit(MonsterContext ctx)
    {
        ctx.Agent.speed = ctx.Config.stat.moveSpeed * ctx.Runtime.SpeedMultiplier;
    }
}
