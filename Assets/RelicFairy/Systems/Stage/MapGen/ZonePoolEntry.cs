using System;

[Serializable]
public class ZonePoolEntry
{
    public string pool_key;
    public string category;
    public string size_tag;
    public int    grid_width;
    public int    grid_height;
    public string grid_csv;
    public float  spawn_local_x;
    public float  spawn_local_z;
    public string theme;
    public string palette;
    public string arena_template_key;
    public float  difficulty_scale;
    public float  scatter_range;
    public float  clear_overlay_delay;
    public int    max_active_spawners;
    public int    stat_version;
}
