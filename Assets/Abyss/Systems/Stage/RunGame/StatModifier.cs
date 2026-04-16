/// <summary>
/// 스탯 종류 정의
/// </summary>
public enum StatType
{
    AttackPower,                // 레거시 — 범용 공격력 아이템/버프용 (Melee+Ranged 동시 적용)
    Defense,
    MaxHp,
    MoveSpeed,
    AttackSpeed,
    SkillCooldownReduction,     // 0.1 = 쿨다운 10% 감소 (아이템/버프 연동용)
    MeleeAttack,                // 근거리 공격력
    RangedAttack,               // 원거리 공격력
    Luck,                       // 행운력
    ActiveItemCooldownReduction, // 액티브 아이템 쿨다운 감소
    Projectile,                 // 투사체 추가 개수 (가산)
    InstantDamage,              // 즉시 피해 (버프가 아닌 즉발 효과)
}

/// <summary>
/// 스탯 수정자 — 어느 스탯을 얼마나 바꾸는지 (가산)
/// </summary>
[System.Serializable]
public struct StatModifier
{
    public StatType Type;
    public float Value;    // 가산량 (양수: 버프, 음수: 디버프)

    public StatModifier(StatType type, float value)
    {
        Type  = type;
        Value = value;
    }
}
