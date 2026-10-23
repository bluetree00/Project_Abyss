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

    // ── 정예 접두어(10-11 · 정예만 · 봉인기부터) — 설계서 §3-4 1차 6종 ──
    Ironclad,    // 강철의 — 정면 120° 피해 −60% · 이동 −15%
    Charger,     // 돌격하는 — 6초마다 9 m 직선 돌진(예고 0.7초) · 끝에 1.2초 멈춤(받는 피해 +30%)
    Summoner,    // 소환하는 — 8초마다 1.2초 의식 → 잡몹 2(살아 있는 졸개 최대 4) · 맞으면 의식이 끊긴다
    Mortar,      // 박격의 — 5초마다 플레이어 자리 + 앞뒤에 원 3개(1.0초 예고) → 폭발
    Thorns,      // 가시의 — 맞을 때마다 가시 1 · 셋이면 0.6초 예고 뒤 주변 폭발
    Avenger,     // 복수의 — 10 m 안 아군이 죽을 때마다 공격 +15% · 이동 +10%(최대 3중첩)
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
        public readonly bool elitePrefix;  // 정예 접두어(정예만 · 시기 특성 굴림에서 빠진다)
        public readonly string prefix;     // 머리 위 이름 앞에 붙는 영문(이름표는 영문 — 09-27)
        public readonly int  sealedMinChapter;   // 봉인기에 나오기 시작하는 챕터(해방기 · 악몽은 Ch1부터)
        public Def(MonsterTraitKind k, MonsterTraitCategory c, string n, string d, string icon, bool lib, bool commonOnly = false)
        {
            kind = k; category = c; name = n; desc = d; iconKey = icon; liberated = lib; this.commonOnly = commonOnly;
            elitePrefix = false; prefix = null; sealedMinChapter = 0;
        }
        public Def(MonsterTraitKind k, MonsterTraitCategory c, string n, string d, string icon, string prefix, int sealedMinChapter)
        {
            kind = k; category = c; name = n; desc = d; iconKey = icon; liberated = true; commonOnly = false;
            elitePrefix = true; this.prefix = prefix; this.sealedMinChapter = sealedMinChapter;
        }
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
        // 정예 접두어 — 이름 · 설명은 첫 조우 안내(한글), prefix는 머리 위 이름(영문)
        new(MonsterTraitKind.Ironclad,  MonsterTraitCategory.Defense, "강철의",    "정면 공격을 막는다 — 옆 · 뒤로 돌아 치자",          "def",   "Ironclad",   1),
        new(MonsterTraitKind.Charger,   MonsterTraitCategory.Attack,  "돌격하는",  "바닥 줄을 따라 돌진한다 — 비킨 뒤 멈춘 틈에 치자", "roll",  "Charging",   1),
        new(MonsterTraitKind.Summoner,  MonsterTraitCategory.Support, "소환하는",  "의식으로 졸개를 부른다 — 치면 의식이 끊긴다",      "skill", "Summoning",  2),
        new(MonsterTraitKind.Mortar,    MonsterTraitCategory.Attack,  "박격의",    "멀리서 원을 떨어뜨린다 — 원 밖으로 · 붙어서 치자", "range", "Bombarding", 2),
        new(MonsterTraitKind.Thorns,    MonsterTraitCategory.Defense, "가시의",    "세 번 맞으면 가시가 터진다 — 치고 빠지자",         "bleed", "Thorned",    3),
        new(MonsterTraitKind.Avenger,   MonsterTraitCategory.Support, "복수의",    "주변 아군이 죽을수록 세진다 — 처치 순서를 고르자", "crit",  "Vengeful",   2),
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
    private const float LiberatedCommonChance = 0.25f;   // 10-22 A10 0.20 → 0.25(해방기 = 새 빌드로 한 단계 높은 판)
    private const float NightmareCommonChance = 0.35f;
    // 봉인기 무작위 정예화(10-21 몬스터 개편 §5) — 일반 · 위협 몹이 챕터 확률로 특성 하나를 받고 챔피언이 된다(체력 · 몸 · 이름 — MonsterTraits).
    // 예고 없는 특성(흔적 · 잠복)과 해방기 몫(속성 저항 · 분열)은 첫 회차에서 뺀다.
    private static readonly float[] SealedChampionChance = { 0f, 0.06f, 0.10f, 0.14f, 0.18f };   // 챕터 0(모름) · 1 · 2 · 3 · 4

    /// <summary>봉인기에도 챔피언 특성으로 나오는가 — 질주 · 폭발 유해(붉은 원 예고) · 수호막 · 지휘.</summary>
    public static bool SealedChampionOk(MonsterTraitKind k)
        => k == MonsterTraitKind.Swift || k == MonsterTraitKind.Volatile || k == MonsterTraitKind.Ward || k == MonsterTraitKind.Commander;

    /// <summary>시기 특성의 이름표 형용사(영문 — 09-27 이름표 규칙). 정예 접두어가 아니면 여기서.</summary>
    private static string EraAdjective(MonsterTraitKind k) => k switch
    {
        MonsterTraitKind.Swift     => "Swift",
        MonsterTraitKind.Volatile  => "Volatile",
        MonsterTraitKind.Trail     => "Smoldering",
        MonsterTraitKind.Ward      => "Warded",
        MonsterTraitKind.Resist    => "Resistant",
        MonsterTraitKind.Commander => "Commanding",
        MonsterTraitKind.Lurker    => "Lurking",
        MonsterTraitKind.Splitter  => "Splitting",
        _                          => null,
    };
    // 정예 접두어 상한(설계서 §7-2 ③) — 토큰 재편 전에도 일반방에 접두어 정예가 몰리지 않게
    private const int   PrefixCapNormalRoom = 1;
    private const int   PrefixCapEliteRoom  = 3;

#if UNITY_EDITOR
    /// <summary>[실측] 정예에 이 접두어를 강제한다(시기 · 챕터 · 상한 무시) — 메뉴 RelicFairy/Monster/Elite Prefix.</summary>
    public static MonsterTraitKind? DebugForcePrefix;
#endif

    /// <summary>
    /// 이 몬스터가 받을 특성(없으면 빈 목록). 일반 · 희귀 = 확률로 1개, 정예 = 해방기 1개 · 악몽 2개(다른 분류). 보스 · 봉인기 = 없음.
    /// <paramref name="roomTaken"/> = 이 방에서 이미 나온 특성 종류 — 방당 종류 2까지(같은 특성 몰림 방지).
    /// </summary>
    public static List<MonsterTraitKind> Roll(MonsterGrade grade, StoryEra era, HashSet<MonsterTraitKind> roomTaken,
                                              MonsterTraitCategory? excludeCategory = null, bool noCommander = false, int maxCount = 2,
                                              int chapter = 0)
    {
        var result = new List<MonsterTraitKind>(2);
        if (grade == MonsterGrade.Boss) return result;
        bool sealedEra = era == StoryEra.Sealed;
        // 봉인기 — 정예는 접두어만, 일반 · 위협은 챕터 확률로 챔피언(챕터를 모르는 호출 = 예전처럼 없음)
        if (sealedEra && grade == MonsterGrade.Elite) return result;
        bool nightmare = era == StoryEra.NightmareMode;

        int want;
        if (grade == MonsterGrade.Elite) want = nightmare ? 2 : 1;
        else if (sealedEra) want = Random.value < SealedChampionChance[Mathf.Clamp(chapter, 0, SealedChampionChance.Length - 1)] ? 1 : 0;
        else want = Random.value < (nightmare ? NightmareCommonChance : LiberatedCommonChance) ? 1 : 0;
        want = Mathf.Min(want, maxCount);   // 접두어 정예는 시기 특성 1개까지(한 몸 최대 2)
        if (want == 0) return result;

        var pool = new List<Def>();
        foreach (var d in All)
        {
            if (d.elitePrefix) continue;   // 접두어는 RollPrefix가 따로 굴린다
            if (!d.liberated && !nightmare) continue;
            if (sealedEra && !SealedChampionOk(d.kind)) continue;
            if (d.commonOnly && grade == MonsterGrade.Elite) continue;
            if (excludeCategory.HasValue && d.category == excludeCategory.Value) continue;   // 접두어와 같은 분류 금지
            if (noCommander && d.kind == MonsterTraitKind.Commander) continue;               // 복수의 정예가 있는 방엔 지휘 없음
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

    /// <summary>
    /// 정예 접두어 하나(없으면 null) — 설계서 §3-4 등장 규칙 · §7-2.
    /// 봉인기 Ch1 = 정예방의 정예 1마리만 · 그 밖엔 정예 전원(일반방 최대 1 · 정예방 최대 3). 같은 접두어는 방당 1.
    /// 봉인기는 접두어마다 시작 챕터가 있다(강철 · 돌격 Ch1 · 소환 · 박격 · 복수 Ch2 · 가시 Ch3). 지휘가 나온 방엔 복수 없음.
    /// </summary>
    public static MonsterTraitKind? RollPrefix(MonsterGrade grade, StoryEra era, int chapter, bool eliteRoom)
    {
        if (grade != MonsterGrade.Elite) return null;
#if UNITY_EDITOR
        if (DebugForcePrefix.HasValue) return DebugForcePrefix.Value;
#endif
        bool sealedEra = era == StoryEra.Sealed;
        if (sealedEra && chapter <= 1 && !eliteRoom) return null;
        int cap = sealedEra && chapter <= 1 ? 1 : eliteRoom ? PrefixCapEliteRoom : PrefixCapNormalRoom;
        if (MonsterRoomLedger.PrefixCount >= cap) return null;

        var pool = new List<Def>();
        foreach (var d in All)
        {
            if (!d.elitePrefix || MonsterRoomLedger.HasPrefix(d.kind)) continue;
            if (sealedEra && chapter < d.sealedMinChapter) continue;
            if (d.kind == MonsterTraitKind.Avenger && MonsterRoomLedger.HasTrait(MonsterTraitKind.Commander)) continue;
            pool.Add(d);
        }
        if (pool.Count == 0) return null;
        return pool[Random.Range(0, pool.Count)].kind;
    }

    /// <summary>머리 위 이름 앞 영문 수식어 — 분류색 · 리치 텍스트. 정예 접두어(Ironclad …) · 시기 특성 형용사(Swift · Volatile … — 10-21 챔피언).</summary>
    public static string PrefixLabel(MonsterTraitKind k)
    {
        var d = Of(k);
        string word = d.elitePrefix ? d.prefix : EraAdjective(k);
        if (string.IsNullOrEmpty(word)) return null;
        return $"<color=#{ColorUtility.ToHtmlStringRGB(CategoryColor(d.category))}>{word}</color>";
    }
}
