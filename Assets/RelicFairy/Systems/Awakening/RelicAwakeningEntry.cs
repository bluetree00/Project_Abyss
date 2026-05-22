using UnityEngine;

/// <summary>
/// RELIC_AWAKENING_DATA 차트 1행.
/// category_id + level 조합이 복합 키이며, 한 레벨에 여러 스탯이 있을 수 있다 (cost=0 보조 행).
/// </summary>
[System.Serializable]
public sealed class RelicAwakeningEntry
{
    public int    index;
    public string category_id;   // "sword" | "shield" | "heart" | "step" | "mana" | "luck"
    public int    level;          // 1-based
    public string stat_type;      // StatType enum 이름 문자열
    public float  value;          // 해당 레벨의 스탯 가산량
    public int    cost;           // 이 레벨 달성에 필요한 심연의 정수 (보조 행은 0)
    public string description;
}

[System.Serializable]
public sealed class RelicAwakeningEntryCollection
{
    public System.Collections.Generic.List<RelicAwakeningEntry> entries;
}

/// <summary>
/// 각성 카테고리 ID 상수.
/// </summary>
public static class AwakeningCategory
{
    public const string Sword  = "sword";   // 검의 각성 — 공격력
    public const string Shield = "shield";  // 방패의 각성 — 방어/체력
    public const string Heart  = "heart";   // 심장의 각성 — 최대 체력 / 흡혈
    public const string Step   = "step";    // 발걸음의 각성 — 이동속도
    public const string Mana   = "mana";    // 마력의 각성 — 스킬 쿨타임
    public const string Luck   = "luck";    // 행운의 각성 — 행운

    public static readonly string[] All = { Sword, Shield, Heart, Step, Mana, Luck };
}
