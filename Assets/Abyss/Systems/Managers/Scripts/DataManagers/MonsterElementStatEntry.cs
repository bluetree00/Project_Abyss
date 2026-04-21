using System.Collections.Generic;

/// <summary>
/// 뒤끝 MONSTER_ELEMENT_STAT_DATA 차트 1행.
/// monster_id 키로 조회 — MonsterBase.ServerStatId 와 매칭.
/// </summary>
[System.Serializable]
public class MonsterElementStatEntry
{
    public string monster_id;
    public string monster_name;
    public string grade;            // Common / Rare / Elite / Boss
    public string attack_type;      // Melee / Ranged
    public string element;          // None / Water / Fire / Grass / Earth / Lightning
    public int    max_hp;
    public float  base_attack;
    public float  base_defense;
    public float  max_accumulation;
    public int    stat_version;
}

[System.Serializable]
public class MonsterElementStatEntryCollection
{
    public List<MonsterElementStatEntry> monsters;
}
