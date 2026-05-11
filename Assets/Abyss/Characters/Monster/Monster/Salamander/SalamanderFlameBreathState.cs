using Abyss.Monster;
using UnityEngine;

/// <summary>
/// 샐러맨더 화염 브레스 특수 상태.
/// HP 70% 이하일 때 반복 발동 — 정면 Cone 범위에 틱 데미지.
/// MovementLockedState 포맷: 이동만 잠금, 방해 가능.
/// </summary>
public class SalamanderFlameBreathState : MovementLockedState<SalamanderBreathData>
{
    private float _breathTimer;
    private float _tickTimer;

    public SalamanderFlameBreathState(SalamanderBreathData data) : base(data) { }

    public override void Enter(MonsterContext ctx)
    {
        ctx.Agent.ResetPath();
        ctx.Agent.velocity = Vector3.zero;

        _breathTimer = Data.breathDuration;
        _tickTimer   = 0f;

        if (ctx.Animator != null && !string.IsNullOrEmpty(Data.breathStateName))
            ctx.Animator.CrossFade(Data.breathStateName, 0.1f);

        Data.SpawnVFX(ctx.Transform);
    }

    public override void Update(MonsterContext ctx)
    {
        _tickTimer   -= Time.deltaTime;
        _breathTimer -= Time.deltaTime;

        if (_tickTimer <= 0f)
        {
            _tickTimer = Data.damageTick;
            ApplyConeDamage(ctx);
        }

        if (_breathTimer <= 0f)
            ctx.Monster.ChangeState<ChaseState>();
    }

    public override void Exit(MonsterContext ctx) { }

    private void ApplyConeDamage(MonsterContext ctx)
    {
        var selfDamageable = ctx.Monster as IDamageable;
        var forward        = ctx.Transform.forward;
        var origin         = ctx.Transform.position;
        var halfAngle      = Data.breathAngle * 0.5f;

        foreach (var col in Physics.OverlapSphere(origin, Data.breathRadius))
        {
            if (col.transform.IsChildOf(ctx.Transform)) continue;

            Vector3 dir = (col.transform.position - origin);
            dir.y = 0f;
            if (dir.sqrMagnitude < 0.001f) continue;

            if (Vector3.Angle(forward, dir.normalized) > halfAngle) continue;

            var player = col.GetComponent<PlayerController>()
                      ?? col.GetComponentInParent<PlayerController>();
            if (player != null)
            {
                player.ApplyKnockback(Vector3.zero, 0f);
                continue;
            }

            var dmg = col.GetComponent<IDamageable>()
                   ?? col.GetComponentInParent<IDamageable>();
            if (dmg != null && dmg != selfDamageable)
                dmg.TakeDamage((int)Data.breathDamage, ctx.Monster.gameObject, 0f);
        }
    }
}
