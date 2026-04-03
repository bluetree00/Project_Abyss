using Abyss.Monster;
using UnityEngine;

/// <summary>
/// 비숍나이트 방패 방어 + 반격 특수 상태.
/// Guard 페이즈: guardDuration 동안 제자리에서 방어 자세 유지.
/// Counter 페이즈: 반격 애니메이션 재생과 동시에 범위 데미지 적용 후 ChaseState 복귀.
/// MovementLockedState 포맷: 이동 잠금, 피격 반응은 허용.
/// </summary>
public class BishopShieldGuardState : MovementLockedState<BishopShieldData>
{
    private enum Phase { Guard, Counter }

    private Phase _phase;
    private float _timer;

    public BishopShieldGuardState(BishopShieldData data) : base(data) { }

    public override void Enter(MonsterContext ctx)
    {
        ctx.Agent.ResetPath();
        _phase = Phase.Guard;
        _timer = Data.guardDuration;
        ctx.Animator?.CrossFade(Data.guardStateName, 0.1f);
    }

    public override void Update(MonsterContext ctx)
    {
        _timer -= Time.deltaTime;

        switch (_phase)
        {
            case Phase.Guard:
                if (_timer <= 0f)
                {
                    _phase = Phase.Counter;
                    _timer = 0.8f;
                    ctx.Animator?.CrossFade(Data.counterStateName, 0.1f);
                    ApplyAreaDamage(ctx, Data.counterRadius, Data.counterDamage);
                }
                break;

            case Phase.Counter:
                if (_timer <= 0f)
                    ctx.Monster.ChangeState<ChaseState>();
                break;
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
