using System.Collections.Generic;

/// <summary>
/// 뒤끝 ITEM_DATA 차트 1행 = 아이템 효과 1슬롯.
/// item_id로 그룹핑하여 다중 효과 아이템 표현.
/// </summary>
[System.Serializable]
public class ItemEntry
{
    public string item_id;
    public string item_name;
    public string rarity;           // Common / Rare / Epic
    public string category;         // Ring, Necklace, Boots, Gloves, Belt, Charm, Active
    public int    slot;
    public string effect_type;      // MeleeAttack, Lifesteal, Heal, DamageAoE ...
    public string trigger;          // Always, OnHit, OnKill, OnUse, OnLowHp ...
    public float  value;
    public float  value2;
    public float  value3;
    public int    max_stack;
    public float  duration;
    public float  cooldown;
    public int    shape_id;         // BLOCK_SHAPE_DATA 참조
    public string icon_key;
    public string description;
    public int    stat_version;
}

[System.Serializable]
public class ItemEntryCollection
{
    public List<ItemEntry> items;
}
