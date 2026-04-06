/// <summary>
/// 무기가 참조하는 공격 스탯 종류.
/// WeaponType에서 변환하여 PlayerRuntimeStats의 MeleeAttack/RangedAttack 중 선택.
/// </summary>
public enum AttackStatKind
{
    Melee,
    Ranged,
}

public static class WeaponTypeExtensions
{
    public static AttackStatKind GetAttackStatKind(this WeaponType type) => type switch
    {
        WeaponType.Bow      => AttackStatKind.Ranged,
        WeaponType.Staff    => AttackStatKind.Ranged,
        WeaponType.Crossbow => AttackStatKind.Ranged,
        _                   => AttackStatKind.Melee,
    };
}
