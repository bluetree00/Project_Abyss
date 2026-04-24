using System;
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
    public string monster_pool_tag; // 쉼표 구분 그룹 번호 (예: "1,2,3,4,5"). 빈 문자열 = 보스/풀 제외
    public int    stat_version;

    // 캐시된 파싱 결과 (최초 접근 시 1회 파싱)
    [NonSerialized] private int[]  _poolTagsCache;
    [NonSerialized] private string _poolTagsCachedFrom;

    /// <summary>monster_pool_tag를 int 배열로 파싱한 결과.
    /// 값이 없거나 파싱 실패 시 빈 배열.</summary>
    public int[] PoolTags
    {
        get
        {
            // monster_pool_tag가 직렬화 후 변했을 가능성까지 고려해 원본 문자열 비교 캐시
            if (_poolTagsCache != null && _poolTagsCachedFrom == monster_pool_tag)
                return _poolTagsCache;

            _poolTagsCache = ParseTags(monster_pool_tag);
            _poolTagsCachedFrom = monster_pool_tag;
            return _poolTagsCache;
        }
    }

    private static int[] ParseTags(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return Array.Empty<int>();

        var parts = raw.Split(',');
        var list = new List<int>(parts.Length);
        foreach (var p in parts)
        {
            if (int.TryParse(p.Trim(), out int n))
                list.Add(n);
        }
        return list.ToArray();
    }
}

[System.Serializable]
public class MonsterElementStatEntryCollection
{
    public List<MonsterElementStatEntry> monsters;
}
