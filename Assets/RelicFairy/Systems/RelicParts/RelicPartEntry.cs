using UnityEngine;

/// <summary>
/// RELIC_PARTS_DATA 차트 1행 = 유물 기억 조각 하나(유물 성장 v2, 10-02).
///
/// 보스를 쓰러뜨릴 때마다 카드 3장 중 하나를 고른다. 조각은 <b>유물 전용</b>이고
/// 가웨인은 시간대(여명 · 정오 · 황혼)에, 랜슬롯은 광기 문턱(10 · 20 · 30 · 40)이나 광란 · 심판에 매달린다(<see cref="anchor"/>).
/// 규칙은 세 줄 — 등급(흐릿 · 선명 · 찬란)이 몇 줄까지 드러나는지를 정한다(숫자가 커지는 게 아니다).
///
/// CSV 컬럼: index | relic_id | part_kind | part_id | part_name | anchor | rune_element | line1 | line2 | line3
///           | memory_line | memory_line_radiant | effect_key | build_family
/// 옛 칸(description · boss_tier · requires)은 v1 행을 읽을 때만 남는다 — v2 행은 <see cref="IsV2"/>로 가른다.
/// 설계: 바탕화면 기획/RelicFairy_유물성장_v2_유물전용·이상상태·반응_20261002.md
/// </summary>
[System.Serializable]
public sealed class RelicPartEntry
{
    public int    index;
    public string relic_id;     // "gawain" | "lancelot"
    public string part_kind;    // "fragment" | "reaction"(v2) — 옛 행은 core/effect/behavior/trigger
    public string part_id;      // 고유 식별자(보유 · 저장 키)
    public string part_name;    // 표시 이름
    public string description;  // [옛] 한 줄 설명 — v2는 line1~3
    public string effect_key;   // 런타임 효과 훅 식별자(RelicPartEffectFactory 분기와 1:1)
    public int    boss_tier;    // [옛] 1 = 기능 · 3 = 코어 — v2는 읽지 않는다
    public string requires;     // [옛] 선행 파츠

    // ── v2 ──
    public string anchor;               // 매달리는 자리(RelicPartAnchor)
    public string rune_element;         // 반응 조각만 — fire | ice | electric | grass | light | dark
    public string line1;                // ① 핵심(흐릿부터)
    public string line2;                // ② 확장(선명부터)
    public string line3;                // ③ 변주(찬란)
    public string memory_line;          // 기억 한 줄(기사 1인칭) — d6 연출
    public string memory_line_radiant;  // 찬란일 때 붙는 둘째 줄
    public string build_family;         // 발동 계열(BuildFamily 이름) — 비면 None

    /// <summary>v2 행인가 — 매달리는 자리가 있어야 한다. 옛 CDN · 옛 캐시 행은 여기서 걸러진다.</summary>
    public bool IsV2 => !string.IsNullOrEmpty(anchor);

    /// <summary>룬 속성과의 반응 조각인가(그 속성 1단계가 켜져 있을 때만 카드에 오른다).</summary>
    public bool IsReaction => anchor == RelicPartAnchor.Reaction;

    /// <summary>등급(1 흐릿 · 2 선명 · 3 찬란)까지 드러난 줄. 1부터.</summary>
    public string Line(int n) => n switch { 1 => line1, 2 => line2, 3 => line3, _ => null };
}

[System.Serializable]
public sealed class RelicPartEntryCollection
{
    public System.Collections.Generic.List<RelicPartEntry> entries;
}

/// <summary>매달리는 자리 상수 — CSV anchor 값과 1:1.</summary>
public static class RelicPartAnchor
{
    // 가웨인 — 시간대(궤적 조각은 두 시간대에 함께 센다)
    public const string Dawn      = "dawn";
    public const string Noon      = "noon";
    public const string Dusk      = "dusk";
    public const string DawnNoon  = "dawn+noon";
    public const string NoonDusk  = "noon+dusk";
    // 랜슬롯 — 광기 문턱 · 광란 · 심판
    public const string R10       = "r10";
    public const string R20       = "r20";
    public const string R30       = "r30";
    public const string R40       = "r40";
    public const string Frenzy    = "frenzy";
    public const string Judgment  = "judgment";
    // 공통
    public const string Reaction  = "reaction";

    /// <summary>가웨인 시간대 셋(공명 · 메아리의 단위).</summary>
    public static readonly string[] GawainPhases = { Dawn, Noon, Dusk };
    /// <summary>랜슬롯 계단 넷(아래부터).</summary>
    public static readonly string[] LancelotRungs = { R10, R20, R30, R40 };

    /// <summary>이 자리가 시간대 <paramref name="phase"/>를 포함하는가(궤적 조각은 두 시간대).</summary>
    public static bool Covers(string anchor, string phase)
    {
        if (string.IsNullOrEmpty(anchor) || string.IsNullOrEmpty(phase)) return false;
        if (anchor == phase) return true;
        return anchor.Contains('+') && (anchor.StartsWith(phase + "+") || anchor.EndsWith("+" + phase));
    }

    /// <summary>카드 배지 · 보유 보기에 쓰는 짧은 이름.</summary>
    public static string Label(string anchor) => anchor switch
    {
        Dawn     => "여명",
        Noon     => "정오",
        Dusk     => "황혼",
        DawnNoon => "여명·정오",
        NoonDusk => "정오·황혼",
        R10      => "광기 10",
        R20      => "광기 20",
        R30      => "광기 30",
        R40      => "광기 40",
        Frenzy   => "광란",
        Judgment => "심판",
        Reaction => "반응",
        _        => anchor ?? string.Empty,
    };
}

/// <summary>파츠 종류 상수.</summary>
public static class RelicPartKind
{
    public const string Fragment = "fragment";   // v2 — 시간대 · 문턱 · 광란 · 심판 조각
    public const string Reaction = "reaction";   // v2 — 룬 속성 반응 조각

    // [옛 v1] — 옛 행을 읽거나 옛 저장을 해석할 때만
    public const string Core     = "core";
    public const string Effect   = "effect";
    public const string Behavior = "behavior";
    public const string Trigger  = "trigger";
    public static readonly string[] Functional = { Effect, Behavior, Trigger };
}
