using System.Collections.Generic;

/// <summary>
/// 뒤끝 BUFF_DATA 차트 1행.
/// buff_type + tier로 같은 효과의 티어별 수치 관리.
/// </summary>
[System.Serializable]
public class BuffEntry
{
    public string buff_id;
    public string buff_type;      // MoveSpeed, AttackPower, Defense, AttackSpeed, Projectile, InstantDamage
    public string stat_type;      // StatType 매핑
    public bool   is_debuff;
    public int    tier;           // 1, 2, 3
    public float  value;
    public bool   is_percent;
    public string description;
    public int    stat_version;
}

[System.Serializable]
public class BuffEntryCollection
{
    public List<BuffEntry> buffs;
}
