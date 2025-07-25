using System.Collections.Generic;

[System.Serializable]
public class MonsterStat
{
    public int monster_id;
    public string type;
    public string monster_name;
    public int level;
    public int max_hp;
    public int attack;
    public float move_speed;
    public float attack_range;
    public float attack_cooldown;
    public float def;
    public int stat_version;
}

[System.Serializable]
public class MonsterStatCollection
{
    public List<MonsterStat> monsters;
}
