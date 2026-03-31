using UnityEngine;
using Abyss.Monster;

/// <summary>
/// 전방 부채꼴(콘) 범위 근거리 공격 SO.
/// 몬스터 전방 halfAngle 각도 이내 + range 거리 이내의 플레이어에게만 판정.
/// </summary>
[CreateAssetMenu(fileName = "ConeAttack", menuName = "Lee/Monster/AttackShape/Cone")]
public class MonsterConeAttackSO : MonsterAttackShapeSO
{
    [Tooltip("콘 최대 거리 (m).")]
    public float range = 2f;

    [Tooltip("전방 기준 반각도 (도). 예: 60 → 좌우 각 60도 = 총 120도 부채꼴.")]
    [Range(0f, 180f)]
    public float halfAngle = 60f;

    public override void Execute(MonsterContext ctx, int damage, float knockbackForce)
    {
        if (ctx.Runtime?.PlayerTarget == null) return;

        Vector3 toPlayer = ctx.Runtime.PlayerTarget.position - ctx.Transform.position;
        if (toPlayer.magnitude > range) return;

        float angle = Vector3.Angle(ctx.Transform.forward, toPlayer);
        if (angle > halfAngle) return;

        var player = ctx.Runtime.PlayerTarget.GetComponent<PlayerController>();
        if (player == null) return;

        player.TakeDamage(damage);

        Vector3 dir = toPlayer.normalized;
        dir.y = 0.3f;
        player.ApplyKnockback(dir.normalized * knockbackForce);
    }
}
