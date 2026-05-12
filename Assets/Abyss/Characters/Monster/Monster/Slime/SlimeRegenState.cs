using Abyss.Monster;
using UnityEngine;

/// <summary>
/// 슬라임 HP 회복 특수 상태.
/// 일정 주기마다 발동 — 이동 멈추고 일정 시간 HP 회복 후 복귀.
/// 피격 시 경직(GetHit)으로 끊긴다.
/// MovementLockedState 포맷: MovementLocked 제약 자동 바인딩.
/// </summary>
public class SlimeRegenState : MovementLockedState<SlimeRegenData>
{
    private float _timer;
    private float _healAccum;

    public SlimeRegenState(SlimeRegenData data) : base(data) { }

    public override void Enter(MonsterContext ctx)
    {
        ctx.Agent.ResetPath();
        _timer    = Data.duration;
        _healAccum = 0f;
        ctx.Animator.CrossFade("Idle_Normal", 0.15f);
        Data.SpawnVFX(ctx.Transform);
    }

    public override void Update(MonsterContext ctx)
    {
        _healAccum += Time.deltaTime;
        if (_healAccum >= 1f)
        {
            ctx.Runtime.CurrentHp = Mathf.Min(
                ctx.Runtime.CurrentHp + Data.healPerSec,
                ctx.Config.stat.maxHp);
            _healAccum -= 1f;
            ctx.Monster.NotifyHPChanged();
        }

        _timer -= Time.deltaTime;
        if (_timer <= 0f)
            ctx.Monster.ChangeState<ChaseState>();
    }

    public override void Exit(MonsterContext ctx) { }
}
