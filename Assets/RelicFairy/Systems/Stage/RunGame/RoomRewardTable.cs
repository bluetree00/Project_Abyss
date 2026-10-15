using UnityEngine;

/// <summary>
/// 방 종류(<see cref="RoomPlanKind"/>) → 클리어 보상 규칙.
///
/// <b>왜 행운(Luck)이 아니라 방 종류인가</b> — 기존 드롭은 <c>LuckRollTable</c> 하나가 드롭 확률(전 레벨 0.6 고정)과
/// 등급을 모두 결정했다. 그런데 플레이어 Luck은 사실상 0에서 움직이지 않아(레벨 0 = Common 100%) 결과적으로
/// <b>모든 방이 "40% 확률로 아무것도 안 나오고, 나오면 전부 Common"</b> 이었다. 정예방도 일반방과 완전히 동일해
/// "정예를 피하는 것이 최적"이라는 결함이 생겼다(통합설계서 §3-2 결함 F).
///
/// → 드롭 확률·등급 분포·후보 수·연료를 <b>방 종류가</b> 정한다. Luck 계열(<see cref="LuckRollService"/>)은
/// 상점·정제소 등 다른 경로에서 계속 쓰이므로 삭제하지 않고 남긴다. 여기서만 참조를 끊는다.
///
/// 정본: 기획/RelicFairy_통합콘텐츠설계서_시뮬에서게임으로_20260801.md §2-2-① · §3-2 · §5 P0-1
/// </summary>
public static class RoomRewardTable
{
    /// <summary>등급 가중치 4단(합이 0이면 Common 폴백).</summary>
    public readonly struct RarityWeights
    {
        public readonly float Common, Rare, Epic, Legendary;

        public RarityWeights(float common, float rare, float epic, float legendary)
        {
            Common = common; Rare = rare; Epic = epic; Legendary = legendary;
        }

        public float Sum => Common + Rare + Epic + Legendary;

        /// <summary>Rare/Epic/Legendary 각각의 확률(0~1). OddsBarView 표시용.</summary>
        public (float rare, float epic, float legendary) Normalized()
        {
            float s = Sum;
            if (s <= 0f) return (0f, 0f, 0f);
            return (Rare / s, Epic / s, Legendary / s);
        }
    }

    /// <summary>한 방이 클리어 시 내놓는 것 전부.</summary>
    public readonly struct Rule
    {
        /// <summary>드롭 자체가 발생할 확률(0~1). 1이면 확정.</summary>
        public readonly float DropChance;
        /// <summary>3지선다 후보 수.</summary>
        public readonly int ChoiceCount;
        /// <summary>등급 하한. null이면 하한 없음.</summary>
        public readonly ItemRarity? RarityFloor;
        /// <summary>등급 분포.</summary>
        public readonly RarityWeights Weights;
        /// <summary>정제소 연료(원석).</summary>
        public readonly int Ore;
        /// <summary>재련소 연료(강화재료).</summary>
        public readonly int EnhanceMaterial;

        public Rule(float dropChance, int choiceCount, ItemRarity? rarityFloor,
                    RarityWeights weights, int ore, int enhanceMaterial)
        {
            DropChance = dropChance; ChoiceCount = choiceCount; RarityFloor = rarityFloor;
            Weights = weights; Ore = ore; EnhanceMaterial = enhanceMaterial;
        }
    }

    // ── 등급 분포 ────────────────────────────────────────────────
    // 한 런에 전투방을 10~20회 돈다. 일반방 분포는 "대부분 Common, 가끔 Rare"가 바닥이고,
    // 정예방은 Rare 하한 위에서 Epic이 실제로 자주 나와야 위험을 감수할 이유가 생긴다.
    private static readonly RarityWeights NormalWeights = new(58f, 31f, 9f, 2f);
    private static readonly RarityWeights EliteWeights  = new(0f, 52f, 36f, 12f);

    // ── Public Methods ───────────────────────────────────────────

    /// <summary>챕터 단계 상한 — Ch1 0 · Ch2 1 · Ch3 2 · Ch4 3.</summary>
    public const int ChapterStepMax = 3;

    /// <summary>
    /// 지금 챕터의 등급 단계 — 사용자 10-10 「좋은 룬의 등장 확률이 진행될수록 적절히 올라가야」. 예전엔 한 런 안에서
    /// Ch1과 Ch4의 일반방 분포가 같았다(시기 · 방 종류만 반영). 런이 없으면 0.
    /// </summary>
    public static int ChapterStep =>
        Mathf.Clamp((int)(GameRunBootstrapper.Instance?.Run?.CurrentChapter ?? ChapterId.Chapter1) - 1, 0, ChapterStepMax);

    /// <summary>
    /// 방 종류별 보상 규칙. 미등록 종류는 일반방 규칙으로 폴백한다.
    /// 챕터(<see cref="ChapterStep"/>)와 시기(해방기 · 악몽 모드, <see cref="EraBalance"/>)가 올라가면 그 단계 합만큼
    /// 룬 등급 가중을 위로 옮긴다(같은 <see cref="EraBalance.ShiftRarity"/>). 연료는 시기만 늘린다(10-01).
    /// </summary>
    public static Rule For(RoomPlanKind kind) => WithAltar(For(kind, ChapterStep, EraBalance.Current, PrefixStep(kind)));

    /// <summary>
    /// 기억의 제단 강화(10-14) — 룬 등급 가중 더하기(Rare · Epic · Legendary %p — 가중 1 = 1%p, 합 100 유지)와 원석 · 강화재료 배율.
    /// Rare 몫은 Common에서, Epic · Legendary 몫은 Common에서 먼저 · 모자라면 Rare에서 옮긴다(정예방은 Common이 0).
    /// Epic · Legendary는 그 등급을 연 뒤에만 몫이 있다(<see cref="MemoryAltarService.EpicOddsBonus"/>). 표시(확률 막대)와 굴림이 같은 값을 본다.
    /// 순수 계산 오버로드(<see cref="For(RoomPlanKind,int,in EraBalance.Spec,int)"/>)엔 넣지 않는다 — 확률 표 도구가 제단 없이 부른다.
    /// </summary>
    private static Rule WithAltar(in Rule r)
    {
        float ra = MemoryAltarService.RareOddsBonus, ep = MemoryAltarService.EpicOddsBonus, lg = MemoryAltarService.LegendOddsBonus;
        float om = MemoryAltarService.OreGainMult, em = MemoryAltarService.EnhanceGainMult;
        if (ra <= 0f && ep <= 0f && lg <= 0f && om <= 1f && em <= 1f) return r;

        var w = r.Weights;
        float c = w.Common, rare = w.Rare, epic = w.Epic, leg = w.Legendary;
        float toRare = Mathf.Min(ra, c);
        c -= toRare; rare += toRare;
        float up = ep + lg;
        if (up > 0f)
        {
            float fromC = Mathf.Min(up, c);
            float fromR = Mathf.Min(up - fromC, rare);
            c -= fromC; rare -= fromR;
            float k = (fromC + fromR) / up;
            epic += ep * k; leg += lg * k;
        }
        return new Rule(r.DropChance, r.ChoiceCount, r.RarityFloor, new RarityWeights(c, rare, epic, leg),
                        ore: Mathf.RoundToInt(r.Ore * om), enhanceMaterial: Mathf.RoundToInt(r.EnhanceMaterial * em));
    }

    /// <summary>접두어 정예가 나온 일반방 = 등급 +1단(방당 최대 1 · 정예방은 이미 Rare 하한이라 없음 — 10-11 정예 접두어).</summary>
    public static int PrefixStep(RoomPlanKind kind)
        => kind == RoomPlanKind.Normal && MonsterRoomLedger.HadPrefixedElite ? 1 : 0;

    /// <summary>순수 계산 — 확률 표 · 시뮬 도구가 챕터 단계 · 시기를 넘겨 부른다(런 상태를 읽지 않는다). <paramref name="bonusStep"/> = 챕터 상한 밖에서 더하는 단계(접두어 정예).</summary>
    public static Rule For(RoomPlanKind kind, int chapterStep, in EraBalance.Spec era, int bonusStep = 0)
    {
        var r    = BaseFor(kind);
        int step = Mathf.Clamp(chapterStep, 0, ChapterStepMax) + era.RarityStep + Mathf.Max(0, bonusStep);
        if (step <= 0 && Mathf.Approximately(era.FuelScale, 1f)) return r;
        var w = r.Weights;
        var (c, ra, e, l) = EraBalance.ShiftRarity(w.Common, w.Rare, w.Epic, w.Legendary, step);
        return new Rule(r.DropChance, r.ChoiceCount, r.RarityFloor, new RarityWeights(c, ra, e, l),
                        ore: Mathf.RoundToInt(r.Ore * era.FuelScale),
                        enhanceMaterial: Mathf.RoundToInt(r.EnhanceMaterial * era.FuelScale));
    }

    /// <summary>확률 막대 머리 오른쪽 한 줄 — 무엇이 등급을 올렸나(「Ch3 +2 · 해방 +1 · 정예」). 없으면 빈 문자열.</summary>
    public static string DescribeBoost(RoomPlanKind kind)
    {
        var sb = new System.Text.StringBuilder();
        int ch = ChapterStep;
        if (ch > 0) sb.Append("Ch").Append(ch + 1).Append(" +").Append(ch);
        int es = EraBalance.Current.RarityStep;
        if (es > 0)
        {
            if (sb.Length > 0) sb.Append(" · ");
            sb.Append(StoryProgress.Era == StoryEra.NightmareMode ? "악몽" : "해방").Append(" +").Append(es);
        }
        if (kind == RoomPlanKind.Elite || kind == RoomPlanKind.MidBoss)
        {
            if (sb.Length > 0) sb.Append(" · ");
            sb.Append(kind == RoomPlanKind.MidBoss ? "정예 보스" : "정예");
        }
        if (PrefixStep(kind) > 0)
        {
            if (sb.Length > 0) sb.Append(" · ");
            sb.Append("접두어 정예 +1");
        }
        float altar = MemoryAltarService.RareOddsBonus + MemoryAltarService.EpicOddsBonus + MemoryAltarService.LegendOddsBonus;
        if (altar > 0f)
        {
            if (sb.Length > 0) sb.Append(" · ");
            sb.Append("제단 +").Append(Mathf.RoundToInt(altar)).Append("%p");
        }
        return sb.ToString();
    }

    private static Rule BaseFor(RoomPlanKind kind) => kind switch
    {
        // 정예 — 확정 드롭 + Rare 하한 + 연료 증량 + 강화재료(§3-2 개편표).
        // 후보는 다른 방과 같은 3장이다 — 룬 선택지를 3장으로 고정했다(10-01 사용자 결정, 예전 4장).
        // 10-15 사용자 「금화보다 재련소와 정제소의 재화를 높여야 — 도박하는 재미」: 원석 6 → 9 · 강화재료 2 → 4(확률은 낮췄다 · EnhanceTable D3)
        RoomPlanKind.Elite => new Rule(1f, 3, ItemRarity.Rare, EliteWeights, ore: 9, enhanceMaterial: 4),

        // 정예 보스(10-12 「관문지기」) — 정예방 위 · 보스방 아래. Epic 하한 · 원석 12(= 정제 한 번) · 강화재료 4.
        // 그 챕터에서 빠진 전투 두 방 + 정예방 몫을 돌려주는 값이다(배치 설계 §5-2). 골드 · 정수는 처치 · 정수 적립이 맡는다.
        RoomPlanKind.MidBoss => new Rule(1f, 3, ItemRarity.Epic, EliteWeights, ore: 16, enhanceMaterial: 6),   // 10-15 12 · 4 → 16 · 6

        // [제거됨 2026-08-12] PreBoss 규칙 —
        // 보스 전 통로는 전 챕터 grid_csv에 스포너가 0개다. 그래서 AttachRoomClearController가
        // RoomWaveController를 아예 안 붙이고, RoomClearGate가 돌지 않는다.
        // 즉 여기 규칙이 있어도 <b>한 번도 호출되지 않았다</b>(보상 0이 실제 동작).
        // 규칙만 남겨두면 "보스 직전에 룬 3지선다 + 원석 4가 나온다"는 잘못된 정보가 되므로 지운다.
        // PreBoss는 결전 직전 숨 고르는 순수 통로로 확정한다 — 보상을 주려면 방 데이터에
        // 스포너를 넣어 전투방으로 만들거나, 별도 지급 경로를 세우는 쪽이 정본이다.

        // 이벤트 — 확정 보상. 등급/개수는 챌린지 성과(ChallengeRewardTable)가 따로 덮어쓴다.
        RoomPlanKind.Event => new Rule(1f, 3, null, NormalWeights, ore: 6, enhanceMaterial: 0),   // 10-15 원석 4 → 6(강화재료는 챌린지 성과)

        // 일반방 및 그 외(상점/재련소/정제소 = 전투 없음 → 이 경로가 호출되지 않음).
        // 확정 드롭 — 예전 0.6 고정은 "방을 깼는데 아무것도 안 나오는" 40%를 만들었고,
        // 그 구간이 런 내 도파민의 가장 큰 구멍이었다(§2-2 원칙 2 "바닥은 안전").
        // 10-15: 원석 4 → 6 · 강화재료 0 → 2 — 강화재료가 이벤트 방에만 몰려 런마다 편차가 컸다.
        _ => new Rule(1f, 3, null, NormalWeights, ore: 6, enhanceMaterial: 2),
    };

    /// <summary>드롭 발생 여부 추첨.</summary>
    public static bool RollDrop(in Rule rule)
    {
        if (rule.DropChance <= 0f) return false;
        if (rule.DropChance >= 1f) return true;
        return Random.value < rule.DropChance;
    }

    /// <summary>가중 추첨으로 등급 1개를 결정한다. 가중치 합이 0 이하면 Common.</summary>
    public static ItemRarity RollRarity(in RarityWeights w)
    {
        float sum = w.Sum;
        if (sum <= 0f) return ItemRarity.Common;

        float roll = Random.Range(0f, sum);

        float cumulative = w.Common;
        if (roll < cumulative) return ItemRarity.Common;

        cumulative += w.Rare;
        if (roll < cumulative) return ItemRarity.Rare;

        // 상위 등급은 기억의 제단에서 열려야 실제로 나온다 — 미해금이면 하위로 강등(정본 §4).
        cumulative += w.Epic;
        if (roll < cumulative) return MemoryAltarService.ClampRarity(ItemRarity.Epic);

        return MemoryAltarService.ClampRarity(ItemRarity.Legendary);
    }
}
