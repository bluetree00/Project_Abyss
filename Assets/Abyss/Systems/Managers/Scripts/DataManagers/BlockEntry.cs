using System.Collections.Generic;

/// <summary>
/// 뒤끝 BLOCK_SHAPE_DATA 차트 1행 = 블록 모양 1개.
/// shape_id로 조회.
/// </summary>
[System.Serializable]
public class BlockShapeEntry
{
    public int    shape_id;
    public string shape_name;
    public string r1;
    public string r2;
    public string r3;
    public string r4;
    public float  cell_size;
    public int    stat_version;
}

[System.Serializable]
public class BlockShapeEntryCollection
{
    public List<BlockShapeEntry> shapes;
}

/// <summary>
/// 뒤끝 BLOCK_GRID_DATA 차트 1행 = 시너지 그리드 효과 1슬롯.
/// grid_id로 그룹핑하여 그리드 모양 + 다중 시너지 효과 표현.
/// </summary>
[System.Serializable]
public class BlockGridEntry
{
    public string grid_id;
    public string grid_name;
    public int    order;
    public int    rows;
    public int    cols;
    public string g1;
    public string g2;
    public string g3;
    public string g4;
    public string g5;
    public string g6;
    public string g7;
    public string g8;
    public int    slot;
    public string effect_type;
    public string trigger;
    public float  value;
    public float  value2;
    public float  value3;
    public int    max_stack;
    public float  duration;
    public string description;
    public int    stat_version;
}

[System.Serializable]
public class BlockGridEntryCollection
{
    public List<BlockGridEntry> grids;
}
