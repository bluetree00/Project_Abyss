using System.Collections.Generic;

/// <summary>
/// RELIC_STAT_DATA 한 행 — 유물 메커닉 수치(슬롯 단위).
/// slot은 각 RelicBehavior의 private const int V_XXX 인덱스와 1:1 대응.
/// CSV 컬럼: index | relic_id | slot | description | value | stat_version
/// </summary>
[System.Serializable]
public sealed class RelicStatEntry
{
    public int    index;
    public string relic_id;
    public int    slot;
    public string description;
    public float  value;
    public int    stat_version;
}

[System.Serializable]
internal sealed class RelicStatEntryCollection
{
    public List<RelicStatEntry> entries;
}
