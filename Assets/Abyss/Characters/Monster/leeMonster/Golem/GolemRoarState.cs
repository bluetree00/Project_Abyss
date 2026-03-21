using UnityEngine;

/// <summary>
/// 골렘 포효 특수 상태.
/// HP가 임계값 이하로 떨어지면 한 번 발동 — 이동 잠금·무적 상태로 포효 후 Chase 복귀.
/// InvincibleState 포맷: Invincible | MovementLocked 제약 자동 바인딩.
/// </summary>
public class GolemRoarState : InvincibleState<GolemRoarData>
{
    private float _timer;

    public GolemRoarState(GolemRoarData data) : base(data) { }

    public override void Enter(LeeMonsterContext ctx)
    {
        ctx.Agent.ResetPath();
        _timer = Data.roarDuration;

        if (ctx.Animator != null && !string.IsNullOrEmpty(Data.roarStateName))
            ctx.Animator.CrossFade(Data.roarStateName, 0.1f);
    }

    public override void Update(LeeMonsterContext ctx)
    {
        _timer -= Time.deltaTime;
        if (_timer <= 0f)
            ctx.Monster.ChangeState<LeeChaseState>();
    }

    public override void Exit(LeeMonsterContext ctx) { }
}
