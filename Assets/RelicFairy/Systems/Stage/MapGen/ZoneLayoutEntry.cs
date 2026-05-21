using System;
using System.Collections.Generic;

/// <summary>
/// CHAPTER_1_ZONE_LAYOUT.csv 한 행 = 존 하나.
/// world_center_x/y/z 기준으로 월드에 배치되며 grid_csv로 맵을 생성한다.
/// </summary>
[Serializable]
public class ZoneLayoutEntry
{
    public int    zone_index;
    public int    layer_index;
    public string category;             // Start, Battle, Elite, Boss, Corridor 등
    public string label;
    public float  world_center_x;
    public float  world_center_y;
    public float  world_center_z;
    public int    grid_width;
    public int    grid_height;
    public string next_zone_indices;    // "|" 구분자 (예: "1|2")
    public float  sink_depth;
    public string platform_prefab_key;
    public float  spawn_local_x;
    public float  spawn_local_z;
    public float  difficulty_scale;
    public bool   has_hidden_reward;
    public float  hidden_reward_local_x;
    public float  hidden_reward_local_y;
    public float  hidden_reward_local_z;
    public string hidden_reward_prefab_key;
    public string arena_template_key;
    public string theme;
    public string palette;              // BlockPalette Addressables 키
    public string grid_csv;
    public string layout_rule;
    public string transition_type;
    public float  scatter_range;
    public float  clear_overlay_delay;
    public int    max_active_spawners;
    public int    stat_version;
    public string corridor_style;
}

[Serializable]
public class ZoneLayoutCollection
{
    public string layout_key;
    public List<ZoneLayoutEntry> zones;
}
