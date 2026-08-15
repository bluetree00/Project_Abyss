/// <summary>원거리 파츠 종류. 축이 서로 겹치지 않게 5종으로 확정(기획 2026-07-20).</summary>
public enum RangedPartKind
{
    /// <summary>분열 — 투사체 수. 임계마다 갈래 +1. (다발 약공 축)</summary>
    Split,
    /// <summary>관통 — 적 통과 수. 임계마다 +1.</summary>
    Pierce,
    /// <summary>폭발 — 착탄 범위 피해.</summary>
    Explode,
    /// <summary>유도 — <b>궤적 조작</b>(에임 보정 아님). 관통과 결합 시 되돌아와 재타격.</summary>
    Homing,
    /// <summary>크기·위력 — 히트박스와 피해가 함께 상승. (단발 강공 축, 분열의 반대)</summary>
    Power,
}

/// <summary>
/// WEAPON_PARTS_DATA 차트 1행 = 원거리 파츠 하나의 정적 정의.
///
/// CSV 컬럼: index | part_id | part_name | description | kind | base_value | per_level | milestone_every | max_level
///          | cost_base | cost_growth
///
/// 효과값은 두 가지로 표현한다:
///  · <b>연속형</b>(폭발 반경·유도 강도·크기) — base_value + per_level × (레벨-1)
///  · <b>계단형</b>(분열·관통) — milestone_every 레벨마다 +1 (임계 돌파 시 계단 상승)
/// </summary>
[System.Serializable]
public sealed class WeaponPartEntry
{
    public int    index;
    public string part_id;
    public string part_name;
    public string description;
    /// <summary>"split" | "pierce" | "explode" | "homing" | "power"</summary>
    public string kind;

    /// <summary>레벨 1에서의 효과값.</summary>
    public float  base_value;
    /// <summary>레벨당 증가분(연속형). 계단형은 0으로 두고 milestone_every를 쓴다.</summary>
    public float  per_level;
    /// <summary>계단형 임계 — 이 레벨 수마다 효과가 1단 오른다. 0이면 계단 없음.</summary>
    public int    milestone_every;
    /// <summary>강화 상한. 0이면 무제한(밸런스상 권장하지 않음).</summary>
    public int    max_level;

    /// <summary>Lv.1 → Lv.2 강화에 드는 강화재료. 0이면 <see cref="DefaultCostBase"/>.</summary>
    public int    cost_base;
    /// <summary>레벨당 비용 배율(복리). 0이면 <see cref="DefaultCostGrowth"/>.</summary>
    public float  cost_growth;

    /// <summary>문자열 kind → enum. 알 수 없으면 Split로 폴백(데이터 오타가 조용히 무효화되지 않게 로그는 매니저가 남긴다).</summary>
    public RangedPartKind Kind => kind switch
    {
        "pierce"  => RangedPartKind.Pierce,
        "explode" => RangedPartKind.Explode,
        "homing"  => RangedPartKind.Homing,
        "power"   => RangedPartKind.Power,
        _         => RangedPartKind.Split,
    };

    /// <summary>
    /// 지정 레벨의 효과값. 계단형이면 임계 돌파 횟수를, 연속형이면 선형 증가를 돌려준다.
    /// </summary>
    public float ValueAt(int level)
    {
        level = UnityEngine.Mathf.Max(1, level);
        if (max_level > 0) level = UnityEngine.Mathf.Min(level, max_level);

        if (milestone_every > 0)
            return base_value + (level - 1) / milestone_every;   // 정수 나눗셈 = 계단

        return base_value + per_level * (level - 1);
    }

    /// <summary>데이터가 비었을 때의 비용 기본값 — 차트 미갱신 상태에서도 강화가 공짜가 되지 않게 한다.</summary>
    private const int   DefaultCostBase   = 3;
    private const float DefaultCostGrowth = 1.35f;

    /// <summary>
    /// <paramref name="level"/> → <paramref name="level"/>+1 강화에 드는 강화재료.
    /// 복리로 급증해 "끝까지 다 올리기"가 불가능하게 만든다 — 어느 파츠를 포기할지가 선택이 된다.
    /// 상한에 도달했으면 0(강화 불가).
    /// </summary>
    public int CostAt(int level)
    {
        if (max_level > 0 && level >= max_level) return 0;

        int   b = cost_base   > 0  ? cost_base   : DefaultCostBase;
        float g = cost_growth > 0f ? cost_growth : DefaultCostGrowth;
        return UnityEngine.Mathf.CeilToInt(b * UnityEngine.Mathf.Pow(g, UnityEngine.Mathf.Max(0, level - 1)));
    }
}

[System.Serializable]
public sealed class WeaponPartEntryCollection
{
    public System.Collections.Generic.List<WeaponPartEntry> entries;
}
