using UnityEngine;
using RelicFairy.Monster;

/// <summary>
/// 구체 범위 근거리 공격 SO.
/// 기존 MonsterBase의 기본 공격 판정과 동일한 동작.
/// radiusOverride 가 0 이하이면 MonsterStatSO.attackRadius 를 사용한다.
/// </summary>
[CreateAssetMenu(fileName = "SphereAttack", menuName = "Lee/Monster/AttackShape/Sphere")]
public class MonsterSphereAttackSO : MonsterAttackShapeSO
{
    [Tooltip("히트 판정 반경 (m). 0 이하면 MonsterStatSO.attackRadius 사용.")]
    public float radiusOverride = 0f;

    public override void Execute(MonsterContext ctx, int damage, float knockbackForce)
    {
        if (ctx.Runtime?.PlayerTarget == null) return;

        float radius = radiusOverride > 0f ? radiusOverride : ctx.Stat.attackRadius;
        float dist   = Vector3.Distance(ctx.Transform.position, ctx.Runtime.PlayerTarget.position);
        if (dist > radius) return;

        var player = ctx.Runtime.PlayerTarget.GetComponent<PlayerController>();
        if (player == null) return;

        SpawnVFX(ctx.Transform);

        player.TakeDamage(damage);

        Vector3 dir = (ctx.Runtime.PlayerTarget.position - ctx.Transform.position).normalized;
        dir.y = 0.3f;
        player.ApplyKnockback(dir.normalized * knockbackForce);
    }
}
