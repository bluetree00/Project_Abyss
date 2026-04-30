using UnityEngine;
using Abyss.Monster;

/// <summary>
/// 원거리 발사체 공격 SO.
/// Execute() 호출 시 projectilePrefab 을 인스턴스화하고
/// MonsterProjectile 컴포넌트에 발사 파라미터를 주입한다.
/// </summary>
[CreateAssetMenu(fileName = "RangedAttack", menuName = "Lee/Monster/AttackShape/Ranged")]
public class MonsterRangedAttackSO : MonsterAttackShapeSO
{
    [Tooltip("발사체 프리팹 (MonsterProjectile 컴포넌트 필수).")]
    public MonsterProjectile projectilePrefab;

    [Tooltip("발사체 이동 속도 (m/s).")]
    public float projectileSpeed = 10f;

    [Tooltip("발사체 최대 비행 거리 (m). 초과 시 자동 소멸.")]
    public float projectileMaxRange = 20f;

    [Tooltip("발사 높이 오프셋. 발사 위치 = 몬스터 위치 + (0, launchHeightOffset, 0).")]
    public float launchHeightOffset = 1f;

    public override void Execute(MonsterContext ctx, int damage, float knockbackForce)
    {
        if (projectilePrefab == null || ctx.Runtime?.PlayerTarget == null) return;

        Vector3 origin    = ctx.Transform.position + Vector3.up * launchHeightOffset;
        Vector3 targetPos = ctx.Runtime.PlayerTarget.position + Vector3.up * launchHeightOffset;
        Vector3 direction = (targetPos - origin).normalized;

        var proj = Object.Instantiate(projectilePrefab, origin, Quaternion.LookRotation(direction));
        proj.Init(direction, projectileSpeed, projectileMaxRange, damage, knockbackForce);
    }
}
