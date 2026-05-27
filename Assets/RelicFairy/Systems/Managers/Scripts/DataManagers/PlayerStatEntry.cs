using System.Collections.Generic;

/// <summary>
/// 뒤끝 PLAYER_DATA 차트 1행 = 캐릭터 1명.
/// </summary>
[System.Serializable]
public class PlayerStatEntry
{
    public string char_id;
    public string char_name;
    public string @class;           // Knight / Berserker / Mage
    public int    max_health;
    public int    base_melee_attack;
    public int    base_ranged_attack;
    public int    base_defense;
    public int    base_luck;
    public float  base_move_speed;
    public float  base_run_speed;
    public float  combo_duration;
    public float  heavy_charge_threshold;
    public float  heavy_release_time;
    public float  dash_speed;
    public float  dash_duration;
    public float  dodge_cooldown;
    public float  jump_force;
    public float  gravity;
    public float  fall_multiplier;
    public float  ground_check_distance;
    public float  air_control_multiplier;
    public float  ground_drag;
    public float  air_drag;
    public string passive_id;
    public int    stat_version;
}

[System.Serializable]
public class PlayerStatEntryCollection
{
    public List<PlayerStatEntry> players;
}

/// <summary>
/// 뒤끝 PASSIVE_DATA 차트 1행 = 패시브 특성 1슬롯.
/// passive_id로 그룹핑.
/// </summary>
[System.Serializable]
public class PassiveEntry
{
    public string passive_id;
    public int    slot;
    public string effect_type;      // Lifesteal, AttackSpeed, Defense, etc.
    public string trigger;          // Always, OnHit, OnKill, OnDodge, OnDamaged
    public float  value;
    public int    max_stack;
    public float  duration;
    public string description;
    public int    stat_version;
}

[System.Serializable]
public class PassiveEntryCollection
{
    public List<PassiveEntry> passives;
}

/// <summary>
/// 뒤끝 EQUIPMENT_DATA 차트 1행 = 장비 1개.
/// </summary>
[System.Serializable]
public class EquipmentEntry
{
    public string weapon_id;
    public string weapon_name;
    public string weapon_type;      // Katana, Greatsword, Crossbow, Bow
    public string rarity;           // Common, Rare, Epic, Legendary (신설; 비어있으면 tier로 폴백)
    public int    tier;
    public float  base_attack;
    public float  base_defense;
    public float  crit_chance;      // 치명타 확률 (%, 0~100)
    public float  crit_damage;      // 치명타 발생 시 데미지 배율 (1.0 = 기본 데미지, 1.25 = +25%, 2.0 = 2배)
    public float  attack_speed;
    public float  attack_range;
    public float  area_of_effect;
    public int    ground_combo_count;
    public int    air_combo_count;
    public float  hold_threshold;
    public string promote_mode;     // None, Stage, ChargeFull
    public int    charge_stages;
    public string weapon_prefab_key;
    public string weapon_display_key;
    public string icon_key;
    public string skill_q_name;
    public float  skill_q_cooldown;
    public string skill_e_name;
    public float  skill_e_cooldown;

    public int    stat_version;
}

[System.Serializable]
public class EquipmentEntryCollection
{
    public List<EquipmentEntry> equipments;
}
