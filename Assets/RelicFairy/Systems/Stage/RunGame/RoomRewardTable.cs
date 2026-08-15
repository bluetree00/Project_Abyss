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

    /// <summary>방 종류별 보상 규칙. 미등록 종류는 일반방 규칙으로 폴백한다.</summary>
    public static Rule For(RoomPlanKind kind) => kind switch
    {
        // 정예 — 확정 드롭 + Rare 하한 + 후보 4 + 연료 증량 + 강화재료(§3-2 개편표).
        RoomPlanKind.Elite => new Rule(1f, 4, ItemRarity.Rare, EliteWeights, ore: 6, enhanceMaterial: 2),

        // [제거됨 2026-08-12] PreBoss 규칙 —
        // 보스 전 통로는 전 챕터 grid_csv에 스포너가 0개다. 그래서 AttachRoomClearController가
        // RoomWaveController를 아예 안 붙이고, RoomClearGate가 돌지 않는다.
        // 즉 여기 규칙이 있어도 <b>한 번도 호출되지 않았다</b>(보상 0이 실제 동작).
        // 규칙만 남겨두면 "보스 직전에 룬 3지선다 + 원석 4가 나온다"는 잘못된 정보가 되므로 지운다.
        // PreBoss는 결전 직전 숨 고르는 순수 통로로 확정한다 — 보상을 주려면 방 데이터에
        // 스포너를 넣어 전투방으로 만들거나, 별도 지급 경로를 세우는 쪽이 정본이다.

        // 이벤트 — 확정 보상. 등급/개수는 챌린지 성과(ChallengeRewardTable)가 따로 덮어쓴다.
        RoomPlanKind.Event => new Rule(1f, 3, null, NormalWeights, ore: 4, enhanceMaterial: 0),

        // 일반방 및 그 외(상점/재련소/정제소 = 전투 없음 → 이 경로가 호출되지 않음).
        // 확정 드롭 — 예전 0.6 고정은 "방을 깼는데 아무것도 안 나오는" 40%를 만들었고,
        // 그 구간이 런 내 도파민의 가장 큰 구멍이었다(§2-2 원칙 2 "바닥은 안전").
        _ => new Rule(1f, 3, null, NormalWeights, ore: 4, enhanceMaterial: 0),
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
