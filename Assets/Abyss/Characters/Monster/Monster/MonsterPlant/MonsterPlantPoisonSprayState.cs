using Abyss.Monster;
using UnityEngine;

/// <summary>
/// 식물 몬스터 독 분사 특수 상태.
/// HP 70% 이하 도달 시 1회 발동 — 제자리에서 독 포자 즉시 분사 후 ChaseState 복귀.
/// FullLockState 포맷: UnInterruptible | MovementLocked 제약 자동 바인딩.
/// </summary>
public class MonsterPlantPoisonSprayState : FullLockState<MonsterPlantSprayData>
{
    private float _timer;

    public MonsterPlantPoisonSprayState(MonsterPlantSprayData data) : base(data) { }

    public override void Enter(MonsterContext ctx)
    {
        ctx.Agent.ResetPath();
        ctx.Agent.velocity = Vector3.zero;

        _timer = Data.sprayDuration;

        if (ctx.Animator != null && !string.IsNullOrEmpty(Data.sprayStateName))
            ctx.Animator.CrossFade(Data.sprayStateName, 0.1f);

        Data.SpawnVFX(ctx.Transform);
        ApplyAreaDamage(ctx, Data.sprayRadius, Data.sprayDamage);
    }

    public override void Update(MonsterContext ctx)
    {
        _timer -= Time.deltaTime;
        if (_timer <= 0f)
            ctx.Monster.ChangeState<ChaseState>();
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
