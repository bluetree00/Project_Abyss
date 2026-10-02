using System.Collections.Generic;
using UnityEngine;
using RelicFairy.Monster;

/// <summary>몬스터 시기 특성(10-02 레벨디자인 설계서 §4 · v3 §4-3) — 1차 8종.</summary>
public enum MonsterTraitKind
{
    Swift,       // 질주 — 이동 속도 +30%(몬스터 공격 속도 배율은 쓰는 곳이 없어 이동만)
    Volatile,    // 폭발 유해 — 죽고 1.2초 뒤 반경 3 m 폭발(붉은 원 예고)
    Trail,       // 흔적 — 지나간 자리 3초 장판
    Ward,        // 수호막 — 체력 20% 몫 동안 받는 피해 −50%
    Resist,      // 속성 저항 — 무작위 속성 하나의 피해 −50%
    Commander,   // 지휘 — 반경 8 m 아군 공격 +20%
    Lurker,      // 잠복 — 5 m 안에 들기 전까지 모습 · 체력바를 감춘다
    Splitter,    // 분열 — 체력 절반에서 작은 분신 둘(일반 몹만 · 악몽)
}

/// <summary>특성 분류 — 한 몬스터에 같은 분류 둘 금지(정예 2개일 때). 배지 테 색이 이 분류색이다.</summary>
public enum MonsterTraitCategory { Attack, Defense, Support, Hinder }

/// <summary>
/// 특성 정의 · 시기별 확률 · 굴림(설계서 §4). 정적 데이터 — 수치 조정은 이 표 한 곳.
/// 봉인기 = 없음 · 해방기 = 일반 20% · 정예 1개 · 악몽 = 일반 35% · 정예 2개(다른 분류) · 보스 · 분신은 없음.
/// </summary>
public static class MonsterTraitTable
{
    public readonly struct Def
    {
        public readonly MonsterTraitKind kind;
        public readonly MonsterTraitCategory category;
        public readonly string name, desc, iconKey;
        public readonly bool liberated;    // 해방기부터(false = 악몽만)
        public readonly bool commonOnly;   // 일반 · 희귀 몹만(분열)
        public Def(MonsterTraitKind k, MonsterTraitCategory c, string n, string d, string icon, bool lib, bool commonOnly = false)
        { kind = k; category = c; name = n; desc = d; iconKey = icon; liberated = lib; this.commonOnly = commonOnly; }
    }

    public static readonly Def[] All =
    {
        new(MonsterTraitKind.Swift,     MonsterTraitCategory.Attack,  "질주",      "이동 속도 +30%",                             "speed",   true),
        new(MonsterTraitKind.Volatile,  MonsterTraitCategory.Attack,  "폭발 유해", "죽고 1.2초 뒤 붉은 원 안이 터진다",          "dmg",     true),
        new(MonsterTraitKind.Trail,     MonsterTraitCategory.Attack,  "흔적",      "지나간 자리에 3초 동안 장판이 남는다",       "fire",    true),
        new(MonsterTraitKind.Ward,      MonsterTraitCategory.Defense, "수호막",    "막이 깨지기 전엔 받는 피해 −50%",            "shield",  true),
        new(MonsterTraitKind.Resist,    MonsterTraitCategory.Defense, "속성 저항", "한 속성의 피해 −50%",                         "element", true),
        new(MonsterTraitKind.Commander, MonsterTraitCategory.Support, "지휘",      "주변 아군 공격 +20% — 먼저 잡자",            "atk",     true),
        new(MonsterTraitKind.Lurker,    MonsterTraitCategory.Hinder,  "잠복",      "가까이 가기 전까지 보이지 않는다",           "dark",    true),
        new(MonsterTraitKind.Splitter,  MonsterTraitCategory.Defense, "분열",      "체력 절반에서 작은 분신 둘을 떼어 낸다",     "special", false, commonOnly: true),
    };

    public static Def Of(MonsterTraitKind kind)
    {
        foreach (var d in All) if (d.kind == kind) return d;
        return All[0];
    }

    /// <summary>분류색 — 공격 붉음 · 방어 청록 · 지원 금 · 방해 보라.</summary>
    public static Color CategoryColor(MonsterTraitCategory c) => c switch
    {
        MonsterTraitCategory.Attack  => new Color(1.00f, 0.38f, 0.32f),
        MonsterTraitCategory.Defense => new Color(0.35f, 0.86f, 0.86f),
        MonsterTraitCategory.Support => new Color(1.00f, 0.82f, 0.40f),
        _                            => new Color(0.72f, 0.48f, 0.96f),
    };

    // ── 시기별 확률(설계서 §4) ────────────────────────────
    private const float LiberatedCommonChance = 0.20f;
    private const float NightmareCommonChance = 0.35f;

    /// <summary>
    /// 이 몬스터가 받을 특성(없으면 빈 목록). 일반 · 희귀 = 확률로 1개, 정예 = 해방기 1개 · 악몽 2개(다른 분류). 보스 · 봉인기 = 없음.
    /// <paramref name="roomTaken"/> = 이 방에서 이미 나온 특성 종류 — 방당 종류 2까지(같은 특성 몰림 방지).
    /// </summary>
    public static List<MonsterTraitKind> Roll(MonsterGrade grade, StoryEra era, HashSet<MonsterTraitKind> roomTaken)
    {
        var result = new List<MonsterTraitKind>(2);
        if (grade == MonsterGrade.Boss || era == StoryEra.Sealed) return result;
        bool nightmare = era == StoryEra.NightmareMode;

        int want;
        if (grade == MonsterGrade.Elite) want = nightmare ? 2 : 1;
        else want = Random.value < (nightmare ? NightmareCommonChance : LiberatedCommonChance) ? 1 : 0;
        if (want == 0) return result;

        var pool = new List<Def>();
        foreach (var d in All)
        {
            if (!d.liberated && !nightmare) continue;
            if (d.commonOnly && grade == MonsterGrade.Elite) continue;
            // 방당 종류 2까지 — 이미 둘이 나왔으면 그 둘 안에서만
            if (roomTaken != null && roomTaken.Count >= 2 && !roomTaken.Contains(d.kind)) continue;
            pool.Add(d);
        }

        for (int i = 0; i < want && pool.Count > 0; i++)
        {
            var pick = pool[Random.Range(0, pool.Count)];
            result.Add(pick.kind);
            roomTaken?.Add(pick.kind);
            pool.RemoveAll(d => d.category == pick.category);   // 같은 분류 둘 금지
        }
        return result;
    }
}
