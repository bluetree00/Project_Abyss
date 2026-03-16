/// <summary>
/// 스탯 종류 정의
/// </summary>
public enum StatType
{
    AttackPower,
    Defense,
    MaxHp,
    MoveSpeed,
    AttackSpeed,
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
