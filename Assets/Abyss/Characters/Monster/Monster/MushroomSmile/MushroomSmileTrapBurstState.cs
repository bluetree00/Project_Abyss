using Abyss.Monster;
using UnityEngine;

/// <summary>
/// 머쉬룸스마일 자폭 특수 상태.
/// 식물 위장 대기 → 플레이어 접근 감지 → 폭발 애니메이션 재생 → 범위 데미지 → DieState 전환.
/// InvincibleState 포맷: 트랩 발동 전 피격 완전 차단.
/// </summary>
public class MushroomSmileTrapBurstState : InvincibleState<MushroomSmileTrapData>
{
    private bool  _triggered;
    private float _delayTimer;

    public MushroomSmileTrapBurstState(MushroomSmileTrapData data) : base(data) { }

    public override void Enter(MonsterContext ctx)
    {
        if (ctx.Agent.isActiveAndEnabled && ctx.Agent.isOnNavMesh)
            ctx.Agent.ResetPath();
        ctx.Animator?.CrossFade(Data.idleStateName, 0.1f);
        _triggered = false;
    }

    public override void Update(MonsterContext ctx)
    {
        if (!_triggered)
        {
            if (ctx.Runtime.PlayerTarget == null) return;

            float dist = Vector3.Distance(ctx.Transform.position, ctx.Runtime.PlayerTarget.position);
            if (dist <= Data.activateRange)
            {
                ctx.Animator?.CrossFade(Data.burstStateName, 0.1f);
                _triggered  = true;
                _delayTimer = Data.burstDelay;
            }
        }
        else
        {
            _delayTimer -= Time.deltaTime;
            if (_delayTimer <= 0f)
            {
                ApplyAreaDamage(ctx, Data.burstRadius, Data.burstDamage);
                ctx.Monster.ChangeState<DieState>();
            }
        }
    }

    public override void Exit(MonsterContext ctx) { }

    private static void ApplyAreaDamage(MonsterContext ctx, float radius, float damage)
    {
        var self = ctx.Monster as IDamageable;
        foreach (var col in Physics.OverlapSphere(ctx.Transform.position, radius))
        {
            if (col.transform.IsChildOf(ctx.Transform)) continue;

            var player = col.GetComponent<PlayerController>()
                      ?? col.GetComponentInParent<PlayerController>();
            if (player != null)
            {
                player.ApplyKnockback(
                    (player.transform.position - ctx.Transform.position).normalized * 3f, 0.3f);
                continue;
            }

            var dmg = col.GetComponent<IDamageable>()
                   ?? col.GetComponentInParent<IDamageable>();
            if (dmg != null && dmg != self)
                dmg.TakeDamage((int)damage, ctx.Monster.gameObject, 2f);
        }
    }
}
