/// <summary>
/// 데미지 공식 — 어빌리티 기본 데미지 + 캐릭터 공격 스탯.
/// 중앙 집중 관리하여 밸런스 조정 시 한 곳만 수정.
/// </summary>
public static class DamageFormula
{
    /// <param name="baseDamage">WeaponAbilitySO.AbilityStep 또는 ColliderStep의 원본 데미지</param>
    /// <param name="attackStat">PlayerRuntimeStats의 MeleeAttack 또는 RangedAttack</param>
    public static float Calculate(float baseDamage, int attackStat)
    {
        return baseDamage + attackStat;
    }
}
