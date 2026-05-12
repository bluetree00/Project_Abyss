using Abyss.Monster;
using UnityEngine;

/// <summary>
/// 가오리 방전 특수 상태.
/// 피격 3회 누적 시 발동 — 진입 즉시 범위 방전 공격 후 AttackReadyState 복귀.
/// UnInterruptibleState 포맷: 방해 불가, 이동 가능.
/// </summary>
public class StingRayDischargeState : UnInterruptibleState<StingRayDischargeData>
{
    private float _timer;

    public StingRayDischargeState(StingRayDischargeData data) : base(data) { }

    public override void Enter(MonsterContext ctx)
    {
        ctx.Agent.ResetPath();
        ctx.Agent.velocity = Vector3.zero;

        _timer = Data.dischargeDuration;

        if (ctx.Animator != null && !string.IsNullOrEmpty(Data.dischargeStateName))
            ctx.Animator.CrossFade(Data.dischargeStateName, 0.1f);

        Data.SpawnVFX(ctx.Transform);
        ApplyAreaDamage(ctx, Data.dischargeRadius, Data.dischargeDamage);
    }

    public override void Update(MonsterContext ctx)
    {
        _timer -= Time.deltaTime;
        if (_timer <= 0f)
            ctx.Monster.ChangeState<AttackReadyState>();
    }

    public override void Exit(MonsterContext ctx) { }

    private static void ApplyAreaDamage(MonsterContext ctx, float radius, float damage)
    {
        var selfDamageable = ctx.Monster as IDamageable;
        foreach (var col in Physics.OverlapSphere(ctx.Transform.position, radius))
        {
            if (col.transform.IsChildOf(ctx.Transform)) continue;

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
                dmg.TakeDamage((int)damage, ctx.Monster.gameObject, 0f);
        }
    }
}
