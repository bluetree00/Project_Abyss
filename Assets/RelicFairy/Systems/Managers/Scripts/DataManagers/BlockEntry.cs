using System.Collections.Generic;

/// <summary>장비/캐릭터 컨셉과 완성 시너지 사이의 친화도 태그.</summary>
public enum SynergyAffinityTag
{
    None      = 0,
    Sword     = 1,
    Axe       = 2,
    Shield    = 3,
    Staff     = 4,
    Wand      = 5,
    Armor     = 6,
    Agility   = 7,
    Heavy     = 8,
    Wind      = 9,
    Gambler   = 10,
    Reckless  = 11,
    Survivor  = 12,
    Tank      = 13,
}

/// <summary>
/// 뒤끝 MERLIN_RUNE_PIECE_DATA 차트 1행 = 룬 조각(배치 블록) 모양 1개.
/// shape_id로 조회.
/// </summary>
[System.Serializable]
public class RunePieceEntry
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
public class RunePieceEntryCollection
{
    public List<RunePieceEntry> shapes;
}

/// <summary>
/// 뒤끝 MERLIN_RUNE_SYNERGY_DATA 차트 1행 = 룬 존 시너지 효과 1단계.
/// zone_id로 그룹핑, threshold(채움 임계값)마다 효과 적용.
/// </summary>
[System.Serializable]
public class RuneSynergyEntry
{
    public string zone_id;
    public string zone_name;
    public int    threshold;
    public string effect_type;
    public string trigger;
    public float  value;
    public float  value2;
    public float  value3;
    public int    max_stack;
    public float  duration;
    public string description;
    public int    stat_version;

    // 구 API 호환 필드 — 새 스키마에서는 기본값(0) 유지
    public int rows;
    public int cols;
    public int order;

    /// <summary>compat: zone_name 별칭.</summary>
    public string grid_name => zone_name;
}

[System.Serializable]
public class RuneSynergyEntryCollection
{
    public List<RuneSynergyEntry> synergies;
}

/// <summary>
/// 뒤끝 MERLIN_RUNE_ZONE_MAP 차트 1행 = 멀린의 룬판 한 행(row).
/// hex_row, pattern(존 코드 문자열 — 문자 1개 = 셀 1개), stat_version.
/// 존 코드(<see cref="ElementDef"/>가 단일 소스): F=불, I=얼음, T=전기, P=풀, L=빛, D=어둠, +=CENTER
/// ⚠️ 구버전 스탯존 코드(A=ATK, D=DEF, H=HP …)는 폐기됐다. Docs의 구 CSV와 혼동 주의.
/// </summary>
[System.Serializable]
public class RuneZoneMapEntry
{
    public int    hex_row;
    public string pattern;
    public int    stat_version;
}

[System.Serializable]
public class RuneZoneMapEntryCollection
{
    public List<RuneZoneMapEntry> rows;
}

