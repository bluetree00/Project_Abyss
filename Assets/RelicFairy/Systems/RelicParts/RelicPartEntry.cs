using UnityEngine;

/// <summary>
/// RELIC_PARTS_DATA 차트 1행 = 유물 파츠 하나.
///
/// 유물 성장(개화)은 런 내 임시 성장으로, 보스 클리어 시 3지선다 드래프트로 획득한다.
/// 수치가 아닌 기능/메커니즘 변화만 담는다("+20% 데미지"는 파츠 아닌 각성/무기 강화가 담당).
///
/// CSV 컬럼: index | relic_id | part_kind | part_id | part_name | description | effect_key | boss_tier
/// </summary>
[System.Serializable]
public sealed class RelicPartEntry
{
    public int    index;
    public string relic_id;     // "gawain" | "lancelot" — 어느 유물의 파츠인지
    public string part_kind;    // "core" | "effect" | "behavior" | "trigger"
    public string part_id;      // 고유 식별자 (드래프트 중복/보유 판정 키)
    public string part_name;    // 표시 이름
    public string description;  // 표시 설명
    public string effect_key;   // 런타임 효과 훅 식별자 (구현은 후속)
    public int    boss_tier;    // 등장 시점: 1 = Ch1·Ch2 기능 파츠 풀 / 3 = Ch3 코어 진화 풀
}

[System.Serializable]
public sealed class RelicPartEntryCollection
{
    public System.Collections.Generic.List<RelicPartEntry> entries;
}

/// <summary>파츠 종류 상수 — CSV part_kind 값과 1:1 대응.</summary>
public static class RelicPartKind
{
    public const string Core     = "core";       // 진화 — 메커니즘 재편(방향 게이트). Ch3 클라이맥스
    public const string Effect   = "effect";     // 새 상태이상·부가 판정
    public const string Behavior = "behavior";   // Q·패시브 작동 방식 변화
    public const string Trigger  = "trigger";    // 새 발동 조건

    /// <summary>기능 파츠(코어 진화 제외) — Ch1·Ch2 드래프트 풀.</summary>
    public static readonly string[] Functional = { Effect, Behavior, Trigger };
}
