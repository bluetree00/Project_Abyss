
namespace RelicFairy.Monster
{
/// <summary>
/// 특수 상태가 활성화됐을 때 몬스터에게 적용되는 제약 플래그.
/// 비트 플래그로 복합 조합 가능.
/// </summary>
[System.Flags]
public enum SpecialStateConstraint
{
    None            = 0,
    UnInterruptible = 1 << 0,  // GetHit 상태 전환 차단 (데미지는 들어옴)
    MovementLocked  = 1 << 1,  // NavMeshAgent 이동 정지
    Invincible      = 1 << 2,  // TakeDamage 자체 무시
}
}
