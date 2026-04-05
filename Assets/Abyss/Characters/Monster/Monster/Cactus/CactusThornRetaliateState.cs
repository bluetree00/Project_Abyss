using Abyss.Monster;
using UnityEngine;

/// <summary>
/// 선인장 가시 반격 특수 상태.
/// 피격 시마다 발동 — 진입 즉시 주변 가시 범위 공격 후 ChaseState 복귀.
/// UnInterruptibleState 포맷: 방해 불가, 이동 가능.
/// </summary>
public class CactusThornRetaliateState : UnInterruptibleState<CactusThornData>
{
    private float _timer;

    public CactusThornRetaliateState(CactusThornData data) : base(data) { }

    public override void Enter(MonsterContext ctx)
    {
        _timer = 0.4f;

        if (ctx.Animator != null && !string.IsNullOrEmpty(Data.thornStateName))
            ctx.Animator.CrossFade(Data.thornStateName, 0.05f);

        ApplyAreaDamage(ctx, Data.thornRadius, Data.thornDamage);
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
