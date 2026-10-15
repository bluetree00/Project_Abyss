using System.Collections.Generic;

/// <summary>
/// 기억의 제단 갈래 — <b>무엇이 넓어지는가</b>(시스템)로 묶는다. 이름만 보고 무엇이 열리는지 알 수 있어야 한다.
/// <para>10-02 재설계 「기억을 모시는 제단」 — 바깥 갈래 넷(룬 · 서약 · 무기 · 여정)이 십자로 퍼지고, 가운데가 유물의 기억이다.
/// 옛 장비 · 원거리 갈래는 무기(근접 + 원거리)와 여정(상점 인장 둘)으로 나뉘어 들어갔다.
/// 값은 저장되지 않는다(노드 정의가 코드에 있다). 베이스캠프 기억 성소의 수정 5개가 이 순서로 대응한다.</para>
/// </summary>
public enum AltarBranch
{
    /// <summary>룬 — 어떤 룬이 나오나</summary>
    Rune,
    /// <summary>서약 — 어떤 서약을 맺나</summary>
    Covenant,
    /// <summary>무기 — 무엇을 들고 싸우나(근접 인장 · 전설 무기 + 석궁 · 원거리 시작 파츠)</summary>
    Weapon,
    /// <summary>여정 — 얼마나 멀리 가나(버티기 · 길의 상점 · 챕터 4 · 심연)</summary>
    Journey,
    /// <summary>유물의 기억 — 가운데. 되찾은 기억 카드를 넓힌다(유물 성장 「공명 그물」과 맞물림)</summary>
    Memory,
}

/// <summary>
/// 노드 크기 등급 — 트리에서 <b>얼마나 크게 그리는가</b>. 판을 바꾸는 노드(열쇠)는 크게, 칸 하나 넓히는 노드는 작게(09-29 제단 개편).
/// </summary>
public enum AltarNodeSize
{
    /// <summary>칸·용량 +1 같은 작은 넓힘</summary>
    Small,
    /// <summary>보통</summary>
    Normal,
    /// <summary>열쇠 — 판을 바꾸는 노드(등급 개방·칸 +1·새 무기·챕터·심연)</summary>
    Keystone,
}

/// <summary>
/// 해금 노드 1개의 정의. <b>정적 데이터라 런타임 상태를 담지 않는다</b> —
/// 해금 여부는 <see cref="UserGameData.unlockedIds"/>, 진척은 <see cref="UserGameData.records"/>가 갖는다.
/// </summary>
public sealed class MemoryAltarNode
{
    public string      Id            { get; }
    public AltarBranch Branch        { get; }
    public string      DisplayName   { get; }
    public string      Description   { get; }

    /// <summary>조건과 무관하게 <b>항상 지불 가능한</b> 가격. 이 값이 데드락 방지의 핵심이다(정본 §2).</summary>
    public int BaseCost { get; }

    /// <summary>할인 조건 기록 키. 비어 있으면 조건 없는 노드(항상 기본가).</summary>
    public string ConditionKey { get; }

    /// <summary>할인 조건 목표치. <see cref="ConditionKey"/> 기록이 이 값 이상이면 할인가가 적용된다.</summary>
    public int ConditionTarget { get; }

    /// <summary>조건 문구(진척 숫자는 런타임에 붙인다).</summary>
    public string ConditionLabel { get; }

    /// <summary>조건 충족 시 가격. 조건이 없으면 <see cref="BaseCost"/>와 같다.</summary>
    public int DiscountCost { get; }

    /// <summary>
    /// 조건이 <b>자물쇠</b>인 예외 노드. 정본 §2-3의 둘(심연 입장·챕터 4)만 true —
    /// 논리적 선후가 있어 데드락이 아니다. 나머지는 전부 false여야 한다.
    /// </summary>
    public bool ConditionRequired { get; }

    /// <summary>
    /// 부모 노드 id. <b>전부 열려 있어야</b> 이 노드를 살 수 있다(선으로 이어진 앞 노드).
    /// 비어 있으면 제단 가운데에 바로 붙는 뿌리 노드다. 같은 갈래 안에서만 잇는다.
    /// </summary>
    public IReadOnlyList<string> Parents { get; }

    /// <summary>화면에서 그리는 크기 등급.</summary>
    public AltarNodeSize Size { get; }

    /// <summary>
    /// 시기 표시(1 봉인기 · 2 해방기 · 3 악몽) — <b>10-14부터 아무것도 막지 않는다</b>(시기 드러남 폐기 · <see cref="MemoryAltarCatalog.IsEraRevealed"/>).
    /// 예전(10-02)엔 드러나지 않은 시기의 노드를 그리지도 팔지도 않았다.
    /// </summary>
    public int Era { get; }

    /// <summary>
    /// 단계 수(10-14 강화형) — 1이면 한 번 사면 끝(해금), 2 이상이면 같은 노드를 단계마다 다시 사서 조금씩 올린다(강화).
    /// 산 단계는 <see cref="UserGameData.unlockedIds"/>에 「id:단계」로 남는다(1단계는 「id」).
    /// </summary>
    public int MaxLevel { get; }

    /// <summary>
    /// 이 노드가 열리는 「갈래 포인트」(10-14 세피리아 재능식) — 그 갈래에서 산 단계 수가 이 값 이상이어야 산다.
    /// 예전엔 선으로 이어진 앞 노드(부모)를 사야 열렸다(트리 깊이) — 사용자 「깊이에 따라 해금은 폐기 · 포인트를 쓰면 열리게」.
    /// 단계 = <see cref="MemoryAltarCatalog.TierPoints"/>(0 · 3 · 8 · 15).
    /// </summary>
    public int RequiredPoints { get; }

    private readonly System.Func<int, string> _valueText;

    /// <summary>단계 <paramref name="lv"/>의 효과 글(0 = 아직 안 산 상태). 강화 노드는 「지금 → 다음」을 이 글 둘로 만든다.</summary>
    public string ValueText(int lv) => _valueText != null ? _valueText(lv) : (lv <= 0 ? "" : Description);

    public bool IsLeveled => MaxLevel > 1;

    /// <summary>
    /// <paramref name="level"/>단계(1부터)를 사는 값. 1단계만 할인 조건이 깎는다(<paramref name="discounted"/>) — 뒤 단계는 기본가 × 곡선.
    /// 곡선 = <see cref="MemoryAltarCatalog.LevelCurve"/>(1 · 1.4 · 1.9 · 2.5 · 3.2) · 10 단위로 맞춘다.
    /// </summary>
    public int CostForLevel(int level, bool discounted)
    {
        if (level <= 1) return discounted ? DiscountCost : BaseCost;
        var curve = MemoryAltarCatalog.LevelCurve;
        float k = curve[System.Math.Min(level, curve.Length) - 1];
        return (int)System.Math.Round(BaseCost * k / 10.0) * 10;
    }

    public MemoryAltarNode(string id, AltarBranch branch, string displayName, string description,
                           int baseCost,
                           string conditionKey = null, int conditionTarget = 0,
                           string conditionLabel = null, int discountCost = 0,
                           bool conditionRequired = false,
                           string[] parents = null,
                           AltarNodeSize size = AltarNodeSize.Normal,
                           int era = 1,
                           int maxLevel = 1,
                           int requiredPoints = 0,
                           System.Func<int, string> valueText = null)
    {
        MaxLevel          = maxLevel < 1 ? 1 : maxLevel;
        RequiredPoints    = requiredPoints < 0 ? 0 : requiredPoints;
        _valueText        = valueText;
        Id                = id;
        Branch            = branch;
        DisplayName       = displayName;
        Description       = description;
        BaseCost          = baseCost;
        ConditionKey      = conditionKey;
        ConditionTarget   = conditionTarget;
        ConditionLabel    = conditionLabel;
        DiscountCost      = discountCost > 0 ? discountCost : baseCost;
        ConditionRequired = conditionRequired;
        Parents           = parents ?? System.Array.Empty<string>();
        Size              = size;
        Era               = era < 1 ? 1 : era > 3 ? 3 : era;
    }

    public bool HasCondition => !string.IsNullOrEmpty(ConditionKey);
    public bool IsRoot       => Parents.Count == 0;
}

/// <summary>
/// 기억의 제단이 파는 것 전량(30노드 · 가운데 유물의 기억 3 + 바깥 갈래 4 — 룬 6 · 서약 4 · 무기 8 · 여정 9, 10-02 재설계).
/// <para><b>왜 CSV가 아니라 코드인가</b> — 차트로 빼면 CDN 스키마 변경이라 조율이 필요하고,
/// 게임플레이 코드가 노드 id를 상수로 읽으므로 오타가 컴파일에서 잡힌다(09-29 사용자 결정 A).</para>
/// <para><b>트리</b>(09-29 개편) — 예전엔 갈래마다 한 줄 사슬이었다. 이제 노드마다 부모를 적고,
/// 화면 자리는 <see cref="MemoryAltarLayout"/>가 갈래 · 깊이 · 가지로 자동으로 잡는다 — 노드 추가는 이 목록에 한 줄이다.</para>
/// </summary>
public static class MemoryAltarCatalog
{
    // ── 강화형(10-14) — 단계 값 곡선 · 갈래 포인트 단계 ──
    /// <summary>강화 단계 값 곡선(1단계 = 기본가) — 5단계까지. 단계가 오를수록 값이 오른다(하데스 「밤의 거울」 결).</summary>
    public static readonly float[] LevelCurve = { 1f, 1.4f, 1.9f, 2.5f, 3.2f };
    /// <summary>갈래 포인트 단계(세피리아 재능 5 · 10 · 20 투자 지점의 결) — 고리 하나 = 이 포인트부터 열리는 칸들.</summary>
    public static readonly int[] TierPoints = { 0, 3, 8, 15 };

    /// <summary>노드가 앉는 고리(1 ~ 4) = 열리는 갈래 포인트 단계.</summary>
    public static int Tier(MemoryAltarNode node)
    {
        if (node == null) return 1;
        int t = 1;
        for (int i = 1; i < TierPoints.Length; i++) if (node.RequiredPoints >= TierPoints[i]) t = i + 1;
        return t;
    }

    // ── 강화 단계별 몫(효과를 읽는 쪽과 노드 글이 같은 값을 본다) ──
    public const float MaxHpPctPerLevel   = 0.04f;   // 최대 체력 +4% — 제단에 남은 유일한 영구 스탯(사용자 10-14 「최대 체력만」)
    public const float RareOddsPerLevel   = 3f;      // Rare 룬 +3%p(Common에서 옮겨 온다)
    public const float EpicOddsPerLevel   = 2f;      // Epic 룬 +2%p(Epic 등장을 연 뒤)
    public const float LegendOddsPerLevel = 1f;      // Legendary 룬 +1%p(Legendary 등장을 연 뒤)
    public const float RefineEpicPerLevel = 0.03f;   // 정제소 Epic +3%p
    public const float SmithPerLevel      = 0.03f;   // 재련 강화 성공률 +3%p
    public const float OreGainPerLevel    = 0.10f;   // 원석 +10%
    public const float EnhanceGainPerLevel = 0.10f;  // 강화재료 +10%
    public const float GoldGainPerLevel   = 0.05f;   // 골드 +5%
    public const float EssenceGainPerLevel = 0.04f;  // 정수 +4%
    public const float ShopDiscountPerLevel = 0.05f; // 상점 가격 -5%
    public const float PotionHealPerLevel = 0.04f;   // 포션 회복 40% → 44 · 48 · 52%(최대 체력 대비, +10%씩)
    public const float DepthRewardPerLevel = 0.05f;  // 심연 깊이마다 정수 +5%

    // ── 기록 키 ─────────────────────────────────────────
    // 해금 할인 조건과 업적 진척이 <b>같은 값</b>을 본다 — 따로 추적하지 않는다(정본 §6).
    public static class Rec
    {
        public const string MaxDepth    = "maxDepth";     // 최고 심연 깊이
        public const string MaxChapter  = "maxChapter";   // 최고 도달 챕터
        public const string EliteKills  = "eliteKills";   // 정예 처치 누적
        public const string BossKills   = "bossKills";    // 보스 처치 누적
        public const string RoomClears  = "roomClears";   // 방 클리어 누적
        public const string MaxEnhance  = "maxEnhance";   // 무기 최고 강화 수치
        public const string ShopUses    = "shopUses";     // 상점 이용 누적
        public const string RefineCount = "refineCount";  // 정제 누적
        public const string Clears      = "clears";       // 완주 누적
        public const string Kills       = "kills";        // 몬스터 처치 누적
        public const string Covenants   = "covenants";    // 서약 맺은 수 누적(런 종료 시 그 런의 보유 수)
        // 10-02 재설계 — 할인 조건을 「심연 깊이」에서 그 고리의 시기 기록으로 바꿨다.
        public const string LibKills    = "libKills";     // 해방기에 시작한 런의 보스 처치 누적
        public const string NmKills     = "nmKills";      // 악몽 모드로 시작한 런의 보스 처치 누적
        /// <summary>봉인한 보스 수(0~4) — 저장값이 아니라 이야기 기록(<see cref="StoryProgress.IsSealed"/>)에서 센다.</summary>
        public const string Seals       = "seals";

        // ── 기행(自發 난이도) — 완주 전에 자발적으로 어렵게 가는 사람을 위한 축 ──
        public const string NoPotionClear   = "noPotionClear";   // 포션 0개로 완주(0/1)
        public const string NoSpecialClear  = "noSpecialClear";  // 특수방 0회로 완주(0/1)
        public const string FlawlessChapter = "flawlessChapter"; // 무피격 챕터 클리어 누적

        /// <summary>각성 6계열 정수 환급을 이미 수행했는가(1회성 마이그레이션 가드).</summary>
        public const string AwakeningRefunded = "awakeningRefunded";

        /// <summary>무형검을 한 번 받았는가(0/1) — 베이스캠프 재설계: 검은 처음 한 번만 소환의 방에서 받고,
        /// 이후엔 베이스캠프가 슬롯0에 자동으로 쥐여 준다. 업적 진척이 아니므로 <see cref="All"/>에 넣지 않는다.</summary>
        public const string SwordAwakened = "swordAwakened";

        /// <summary>제단 트리에서 한 번이라도 드러난 가장 깊은 고리(연출 가드 — 새 고리가 처음 드러날 때만 고리를 그린다).
        /// 업적 진척이 아니므로 <see cref="All"/>에 넣지 않는다.</summary>
        public const string AltarRingSeen = "altarRingSeen";

        /// <summary>제단에서 고리가 드러나는 연출을 본 가장 늦은 시기(1~3) — 새 시기 첫 방문에만 고리를 그린다(10-02). 업적 진척 아님.</summary>
        public const string AltarEraSeen = "altarEraSeen";

        /// <summary>10-02 재설계로 뺀 유물 노드 4개의 정수 환급을 마쳤는가(1회성 가드). 업적 진척 아님.</summary>
        public const string RemovedNodesRefunded = "altarRefund1002";
        public const string RemovedNodesRefunded1014 = "altarRefund1014";   // 「챕터 4 개방」 걷음 환급(1회)
        public const string LevelsReset1014 = "altarLevels1014";   // 강화형 전환 리셋 · 환급(1회 — 사용자 「출시 전이니 리셋」)

        /// <summary>업적 진척으로 흘려보낼 기록 키 전량. 내부 가드(AwakeningRefunded)는 제외한다.</summary>
        public static readonly string[] All =
        {
            MaxDepth, MaxChapter, EliteKills, BossKills, Kills,
            RoomClears, MaxEnhance, ShopUses, RefineCount, Clears, Covenants, LibKills, NmKills,
            NoPotionClear, NoSpecialClear, FlawlessChapter,
        };
    }

    // ── 노드 id ─────────────────────────────────────────
    public const string WeaponCrossbow = "weapon_crossbow";
    // 시작 원거리 파츠 — 분열의 시위만 처음부터 열려 있고 나머지 넷은 여기서 연다(09-27 사용자 결정).
    public const string PartPierce     = "part_pierce_unlock";
    public const string PartPower      = "part_power_unlock";
    public const string PartHoming     = "part_homing_unlock";
    public const string PartExplode    = "part_explode_unlock";
    // ⚠️ 카타나·대검·활은 <b>노드가 아니다.</b>
    //   · 활  = 기본 지급 원거리 무기.
    //   · 카타나·대검 = 무형검(T0_Nameless)의 <b>런 중 진화 분기</b>(NamelessEvolution). 매 런 무료로 열려 있다.
    //     시나리오상 무형검은 "무엇이든 될 수 있는" 검이라 형(形)은 런 안에서 벼려야 하고,
    //     그것을 정수로 파는 순간 "이미 있는 걸 잠갔다 푸는 지연"이 된다.
    public const string PartsInherit   = "parts_inherit";
    public const string SigilMerchant  = "sigil_merchant";
    public const string SigilSmith     = "sigil_smith";
    public const string SigilAscetic   = "sigil_ascetic";

    // 「룬 선택지 +1」(rune_choice_4)은 10-01 폐지 — 룬 선택지는 3장 고정. 옛 세이브에 id가 남아 있어도 읽는 곳이 없다.
    // ⚠️ 10-02 재설계로 카탈로그에서 뺀 유물 노드 넷(PartsDraft4 · CorePartsTier1 · CorePartsAll · PartsInherit) — 상수는 옛 드래프트 경로가
    //    아직 읽어서 남겨 둔다. 카탈로그에 없으니 IsUnlocked는 늘 false(후보 3 · 코어 1종 · 이어받기 없음). 유물 성장 「공명 그물」 반입 때 같이 걷는다.
    public const string PartsDraft4    = "parts_draft_4";
    public const string RefineQuality  = "refine_quality";
    public const string RuneEpic       = "rune_epic";
    public const string CorePartsTier1 = "core_parts_1";
    public const string RuneLegendary  = "rune_legendary";
    public const string CorePartsAll   = "core_parts_all";
    public const string WeaponEvolve   = "weapon_evolve";

    public const string CovenantParts1 = "covenant_parts_1";
    public const string CovenantSlot   = "covenant_slot";
    public const string CovenantParts2 = "covenant_parts_2";   // 10-14 걷음 — 「서약 카드」(covenant_parts_1) 3단계로 합쳤다
    public const string CovenantParts3 = "covenant_parts_3";

    public const string Revive         = "revive_once";
    public const string MaxHpUp        = "max_hp_up";

    public const string AbyssDepth     = "abyss_depth";
    public const string Chapter4       = "chapter_4";   // 10-14 걷음 — 카탈로그에 없다 · 산 세이브는 RemovedNodes1014로 환급
    public const string DepthReward    = "depth_reward";

    // ── 판 넓히기(09-29 개편 신설) — 런의 칸 · 선택지를 넓힌다. 영구 스탯은 없다 ──
    public const string RuneStorage1   = "rune_storage_1";   // 룬 보관함 5 → 6
    public const string RuneStorage2   = "rune_storage_2";   // 10-14 걷음 — 「룬 보관함」(rune_storage_1) 2단계로 합쳤다
    public const string RefinePick     = "refine_pick";      // 정제 1개 → 2장 중 고르기
    public const string ShopReroll     = "shop_reroll";      // 상점 새로고침 개방
    public const string StartParts2    = "start_parts_2";    // 시작 파츠 1 → 2개
    public const string PotionSlot     = "potion_slot";      // 포션 3 → 4칸

    // ── 강화형(10-14) 신설 — 단계로 조금씩 오르는 것 ──
    public const string RuneRareOdds   = "rune_rare_odds";     // Rare 룬 확률 +3%p/단계
    public const string RuneEpicOdds   = "rune_epic_odds";     // Epic 룬 확률 +2%p/단계
    public const string RuneLegendOdds = "rune_legend_odds";   // Legendary 룬 확률 +1%p/단계
    public const string OreGain        = "ore_gain";           // 원석 +10%/단계
    public const string EnhanceGain    = "enhance_gain";       // 강화재료 +10%/단계
    public const string GoldGain       = "gold_gain";          // 골드 +5%/단계
    public const string EssenceGain    = "essence_gain";       // 정수 +4%/단계
    public const string PotionHeal     = "potion_heal";        // 포션 회복 +4%p/단계(40% 기준 +10%)

    // ── 가운데 · 유물의 기억(10-02 재설계) — 유물 성장 「공명 그물」의 기억 카드를 넓힌다 ──
    public const string MemRedraw      = "mem_redraw";       // 런당 1회 기억 카드 3장 다시 굴리기
    public const string MemClear       = "mem_clear";        // 기억 카드 등급 띠 한 단
    public const string MemRadiant     = "mem_radiant";      // 10-14 걷음 — 「기억 카드 등급」(mem_clear) 2단계로 합쳤다(옛 세이브 환급 표에만)

    /// <summary>
    /// 가운데 기억 노드를 살 수 있는가. 기억 카드 다시 굴리기 · 등급 띠는 유물 성장 「공명 그물」(f7)이 받아야 효과가 있다 —
    /// 그 전에 팔면 없는 확장을 파는 것이라 잠가 둔다(「유물 성장 개편과 함께 열린다」). f7 반입 때 true로 바꾼다.
    /// </summary>
    public const bool MemoryNodesLive = true;   // 유물 성장 v2 반입(10-02 f7) — 다시 떠올리기 · 등급 띠를 composer가 읽는다

    /// <summary>10-02 재설계로 뺀 노드와 환급 정수(옛 기본가 — 할인가로 샀어도 기본가를 돌려준다).</summary>
    public static readonly (string id, int refund)[] RemovedNodes =
    {
        (PartsDraft4, 2200), (CorePartsTier1, 3000), (CorePartsAll, 4200), (PartsInherit, 4800),
    };

    /// <summary>10-14 뺀 노드와 환급 정수 — 「챕터 4 개방」(사용자 「챕터 해금은 빼고 바로 갈 수 있게」). 10-02 환급을 이미 받은 세이브도 받도록 기록 키를 따로 쓴다.</summary>
    public static readonly (string id, int refund)[] RemovedNodes1014 =
    {
        (Chapter4, 800),
    };

    private static readonly MemoryAltarNode[] Nodes =
    {
        // 10-14 강화형 v2(설계 `기획/RelicFairy_기억의제단_강화형_개편안_20261014.md`) — 사용자 「강화로 조금씩」 · 「깊이 대신 포인트를 쓰면 열리게(세피리아)」.
        // · 강화 노드 = 단계(maxLevel ≥ 2) — 단계 하나 = 그 갈래 포인트 1. 해금 노드 = 한 번(maxLevel 1) — 이것도 포인트 1.
        // · 열림 = 그 갈래 포인트가 requiredPoints(0 · 3 · 8 · 15) 이상. 부모(parents)는 뜻으로 묶인 것만(Epic 확률 ← Epic 등장 등).
        // · 시기 고리(era)는 그대로 — 붕괴 · 엔딩 전엔 그 칸을 그리지도 팔지도 않는다(스포일러 금지).
        // · 값 합 ≈ 73,900(사용자 결정 「약 72,000」) · 1단계 값 220 ~ 560 — 귀환마다 무엇이든 하나는 오른다.

        // ── 가운데 · 유물의 기억 ─────────────────────────
        new(MemRedraw,  AltarBranch.Memory, "다시 떠올리기", "런당 1회 — 보스를 쓰러뜨린 뒤 기억 카드 3장을 다시 굴린다", 1400,
            Rec.Seals, 2, "보스 2명 봉인", 900, size: AltarNodeSize.Keystone, era: 1),
        new(MemClear,   AltarBranch.Memory, "기억 카드 등급", "기억 카드 등급 띠 — 선명 · 찬란이 더 자주 뜬다", 1450,
            Rec.LibKills, 2, "해방된 보스 2회 처치", 950,
            parents: new[] { MemRedraw }, size: AltarNodeSize.Keystone, era: 2, maxLevel: 2,
            valueText: lv => lv <= 0 ? "기본 등급 띠" : lv == 1 ? "선명 · 찬란이 더 자주" : "찬란이 더 자주"),

        // ── 룬 — 어떤 룬이 나오나 ─────────────────────────
        new(RuneStorage1,   AltarBranch.Rune, "룬 보관함",   "룬 보관함 칸",              560,
            Rec.RoomClears, 60, "방 60회 클리어", 360, size: AltarNodeSize.Small, maxLevel: 2,
            valueText: lv => $"룬 보관함 {5 + lv}칸"),
        new(RuneRareOdds,   AltarBranch.Rune, "Rare 룬 확률", "방 보상 Rare 룬 확률", 250, maxLevel: 5,
            valueText: lv => $"Rare 룬 +{RareOddsPerLevel * lv:0}%p"),
        new(OreGain,        AltarBranch.Rune, "원석 획득",   "방 보상 원석", 220, size: AltarNodeSize.Small, maxLevel: 5,
            valueText: lv => $"원석 +{OreGainPerLevel * 100f * lv:0}%"),
        new(RuneEpic,       AltarBranch.Rune, "Epic 룬 등장",   "룬 최고 등급  Rare → Epic",        1900,
            Rec.Seals, 3, "보스 3명 봉인", 1250, size: AltarNodeSize.Keystone, requiredPoints: 3),
        new(RuneEpicOdds,   AltarBranch.Rune, "Epic 룬 확률", "방 보상 Epic 룬 확률", 380,
            parents: new[] { RuneEpic }, maxLevel: 5, requiredPoints: 3,
            valueText: lv => $"Epic 룬 +{EpicOddsPerLevel * lv:0}%p"),
        new(RefinePick,     AltarBranch.Rune, "정제 두 장",     "정제할 때  룬 1개 → 2장 중 고르기", 1400,
            Rec.RefineCount, 10, "정제 10회", 900, requiredPoints: 8),
        // 영웅 룬 <b>뒤</b>여야 한다 — 영웅이 잠겨 있으면 정제소도 영웅을 낼 수 없어(등급 제한) 사 봐야 효과가 없다.
        new(RefineQuality,  AltarBranch.Rune, "정제 등급", "정제소 Epic 확률",          380,
            Rec.RefineCount, 30, "정제 30회", 250,
            parents: new[] { RuneEpic }, maxLevel: 5, requiredPoints: 8,
            valueText: lv => $"정제소 Epic +{RefineEpicPerLevel * 100f * lv:0}%p"),
        new(RuneLegendary,  AltarBranch.Rune, "Legendary 룬 등장", "룬 최고 등급  Epic → Legendary",        2500,
            Rec.LibKills, 3, "해방된 보스 3회 처치", 1600,
            parents: new[] { RuneEpic }, size: AltarNodeSize.Keystone, era: 2, requiredPoints: 15),
        new(RuneLegendOdds, AltarBranch.Rune, "Legendary 룬 확률", "방 보상 Legendary 룬 확률", 720,
            parents: new[] { RuneLegendary }, era: 2, maxLevel: 3, requiredPoints: 15,
            valueText: lv => $"Legendary 룬 +{LegendOddsPerLevel * lv:0}%p"),

        // ── 서약 — 어떤 서약을 맺나 ───────────────────────
        // 원인·효과 카드는 네 단계로 열린다(<see cref="CovenantPalette"/>의 개방 단계 0 ~ 3 = 이 노드의 단계).
        new(CovenantParts1, AltarBranch.Covenant, "서약 카드", "서약 원인 · 효과 종류",   820,
            Rec.Covenants, 5, "서약 5번 맺기", 530, maxLevel: 3,
            valueText: lv => lv switch
            {
                <= 0 => "원인 5 · 효과 7종",
                1    => "원인 8 · 효과 9종",
                2    => "원인 9 · 효과 12종",
                _    => "원인 9 · 효과 15종",
            }),
        // 「한 장의 서약서」(10-02) — id는 그대로(세이브), 뜻만 「서약 칸 +1」 → 「다섯째 절」
        new(CovenantSlot,   AltarBranch.Covenant, "다섯째 절",    "서약서 한 문장의 결과  4 → 5절", 2000,
            Rec.Covenants, 25, "서약 25번 맺기", 1300,
            size: AltarNodeSize.Keystone, era: 2, requiredPoints: 3),

        // ── 무기 — 무엇을 들고 싸우나(근접 + 원거리) ─────────
        // 주무기는 항상 무형검이고(시나리오), 카타나·대검은 런 안의 진화 분기라 노드가 아니다.
        // 원거리 시작 파츠는 베이스캠프 파츠 작업대에서 매 런 Lv1로 들고 나간다 — 분열의 시위는 기본으로 열려 있다(사용자 결정).
        new(PartPierce,     AltarBranch.Weapon, "관통의 촉",        "시작 파츠  +관통",                450,
            Rec.RoomClears, 30, "방 30회 클리어", 300),
        new(SigilSmith,     AltarBranch.Weapon, "재련 성공률",  "재련 강화 성공률",          400,
            Rec.MaxEnhance, 12, "무기 +12 도달", 260, maxLevel: 3,   // 10-15 강화 단계 세분(옛 +6)
            valueText: lv => $"재련 성공률 +{SmithPerLevel * 100f * lv:0}%p"),
        new(EnhanceGain,    AltarBranch.Weapon, "강화재료 획득",  "방 보상 강화재료", 340, size: AltarNodeSize.Small, maxLevel: 3,
            valueText: lv => $"강화재료 +{EnhanceGainPerLevel * 100f * lv:0}%"),
        new(WeaponCrossbow, AltarBranch.Weapon, "석궁",             "시작 원거리 무기  활 → 활·석궁",  900,
            Rec.EliteKills, 5, "정예 5회 처치", 600, size: AltarNodeSize.Keystone, requiredPoints: 3),
        new(PartPower,      AltarBranch.Weapon, "거력의 축",        "시작 파츠  +거력",                1200,
            Rec.EliteKills, 12, "정예 12회 처치", 800, requiredPoints: 3),
        new(PartHoming,     AltarBranch.Weapon, "추적의 깃",        "시작 파츠  +추적",                1500,
            Rec.LibKills, 1, "해방된 보스 1회 처치", 950, era: 2, requiredPoints: 8),
        // 시작 파츠가 셋(분열·관통·거력)은 있어야 「둘 들고 가기」가 고르는 일이 된다.
        new(StartParts2,    AltarBranch.Weapon, "시작 파츠 둘",     "시작 파츠  1개 → 2개 들고 가기",  2200,
            Rec.EliteKills, 40, "정예 40회 처치", 1400,
            parents: new[] { PartPower }, era: 2, requiredPoints: 8),
        // 승급 자체는 해금 없이도 된다(강화 MAX면 가능) — 해금이 넓히는 것은 <b>후보의 수</b>다.
        new(WeaponEvolve,   AltarBranch.Weapon, "전설 무기 3종", "승급할 전설 무기  1종 → 3종",     2600,
            Rec.LibKills, 3, "해방된 보스 3회 처치", 1650, size: AltarNodeSize.Keystone, era: 2, requiredPoints: 8),
        new(PartExplode,    AltarBranch.Weapon, "작렬의 탄두",      "시작 파츠  +작렬",                2400,
            Rec.NmKills, 1, "악몽 보스 1회 처치", 1550, era: 3, requiredPoints: 8),

        // ── 여정 — 얼마나 멀리 가나(버티기 · 재화 · 길의 상점 · 심연) ─────────
        // 버티는 쪽은 조건 없음: 벽을 넘게 해주는 것이라 무조건 열려야 한다.
        new(Revive,         AltarBranch.Journey, "부활 1회",       "런당 1회 — 쓰러지면 최대 체력 50%로 일어나 2초 무적",   800,
            size: AltarNodeSize.Keystone),
        new(MaxHpUp,        AltarBranch.Journey, "최대 체력",  "최대 체력",                 440, maxLevel: 5,
            valueText: lv => $"최대 체력 +{MaxHpPctPerLevel * 100f * lv:0}%"),
        new(SigilMerchant,  AltarBranch.Journey, "상점 가격",    "상점 가격",                 360,
            Rec.ShopUses, 5, "상점 5회 이용", 240, maxLevel: 3,
            valueText: lv => lv <= 0 ? "상점 가격 그대로" : $"상점 가격 -{ShopDiscountPerLevel * 100f * lv:0}%"),
        new(GoldGain,       AltarBranch.Journey, "골드 획득",    "얻는 골드",                 230, size: AltarNodeSize.Small, maxLevel: 5,
            valueText: lv => $"골드 +{GoldGainPerLevel * 100f * lv:0}%"),
        new(PotionHeal,     AltarBranch.Journey, "포션 회복량",  "포션 한 병 회복량",           360, maxLevel: 3, requiredPoints: 3,
            valueText: lv => $"포션 회복 최대 체력의 {(0.40f + PotionHealPerLevel * lv) * 100f:0}%"),
        new(EssenceGain,    AltarBranch.Journey, "정수 획득",    "얻는 심연의 정수",            400, maxLevel: 5, requiredPoints: 3,
            valueText: lv => $"정수 +{EssenceGainPerLevel * 100f * lv:0}%"),
        new(ShopReroll,     AltarBranch.Journey, "상점 새로고침",  "상점 진열 새로고침(10골드)",   820,
            Rec.ShopUses, 10, "상점 10회 이용", 530, size: AltarNodeSize.Small, maxLevel: 2, requiredPoints: 3,
            valueText: lv => lv <= 0 ? "새로고침 없음" : $"상점마다 {lv}회"),
        new(PotionSlot,     AltarBranch.Journey, "포션 칸",     "포션 칸",                1100,
            size: AltarNodeSize.Small, era: 2, maxLevel: 2, requiredPoints: 8,
            valueText: lv => $"포션 {3 + lv}칸"),
        // 「심연 입장」만 조건이 <b>자물쇠</b>다 — 심연(순환)은 악몽기 리치를 쓰러뜨린 엔딩 뒤에 열린다(정본 §2-3).
        new(AbyssDepth,     AltarBranch.Journey, "심연 입장",      "엔딩 뒤 심연 개방 — 깊이마다 적 체력 · 공격력 +25%",  1000,
            StoryProgress.Rec.Ending, 1, "성소의 주인을 완전히 쓰러뜨리기", 1000, conditionRequired: true,
            size: AltarNodeSize.Keystone, era: 3, requiredPoints: 8),
        new(DepthReward,    AltarBranch.Journey, "깊이 보상",      "심연 깊이마다 정수",       720,
            Rec.NmKills, 4, "악몽 보스 4회 처치", 470,
            parents: new[] { AbyssDepth }, era: 3, maxLevel: 3, requiredPoints: 15,
            valueText: lv => $"깊이마다 정수 +{DepthRewardPerLevel * 100f * lv:0}%"),
        new(SigilAscetic,   AltarBranch.Journey, "고행자의 인장",  "보상 선택지 -1 · 정수 ×1.6 (켜고 끄기)", 3200,
            Rec.NmKills, 8, "악몽 보스 8회 처치", 2100,
            parents: new[] { AbyssDepth }, size: AltarNodeSize.Keystone, era: 3, requiredPoints: 15),
    };

    /// <summary>
    /// 10-14 강화형 전환 — 사용자 「아직 출시 전이니 리셋」. 옛 노드(한 번 사는 29칸)에 쓴 정수를 옛 기본가로 돌려주고 산 칸을 비운다(1회 · 기록 키).
    /// 강화로 합쳐진 노드(룬 보관함 +2 · 찬란한 기억 · 서약 카드 +4/+3)도 여기 든다.
    /// </summary>
    public static readonly (string id, int refund)[] LegacyNodes1014 =
    {
        (MemRedraw, 1400), (MemClear, 2600), (MemRadiant, 4500),
        (RuneStorage1, 700), (RefinePick, 1800), (RuneEpic, 2600), (RuneLegendary, 3000), (RefineQuality, 2700), (RuneStorage2, 3000),
        (CovenantParts1, 1600), (CovenantSlot, 2400), (CovenantParts2, 2100), (CovenantParts3, 3200),
        (PartPierce, 450), (WeaponCrossbow, 900), (PartPower, 1200), (SigilSmith, 1500), (PartHoming, 1500),
        (StartParts2, 2300), (WeaponEvolve, 2800), (PartExplode, 2600),
        (Revive, 800), (SigilMerchant, 1300), (ShopReroll, 1600), (MaxHpUp, 1400), (PotionSlot, 1900),
        (AbyssDepth, 1000), (DepthReward, 3400), (SigilAscetic, 4200),
    };

    private static Dictionary<string, MemoryAltarNode>       _byId;
    private static Dictionary<string, List<MemoryAltarNode>> _children;
    private static Dictionary<string, int>                   _depth;

    public static IReadOnlyList<MemoryAltarNode> All => Nodes;

    public static MemoryAltarNode Get(string id)
    {
        if (string.IsNullOrEmpty(id)) return null;

        if (_byId == null)
        {
            _byId = new Dictionary<string, MemoryAltarNode>(Nodes.Length);
            foreach (var n in Nodes)
                _byId[n.Id] = n;
        }
        return _byId.TryGetValue(id, out var node) ? node : null;
    }

    /// <summary>
    /// 이 노드를 부모로 두는 노드들(정의 순서). 산 노드에서 선이 뻗어 나가는 방향이다.
    /// </summary>
    public static IReadOnlyList<MemoryAltarNode> Children(MemoryAltarNode node)
    {
        if (node == null) return System.Array.Empty<MemoryAltarNode>();
        if (_children == null)
        {
            _children = new Dictionary<string, List<MemoryAltarNode>>(Nodes.Length);
            foreach (var n in Nodes)
                foreach (var p in n.Parents)
                {
                    if (!_children.TryGetValue(p, out var list)) _children[p] = list = new List<MemoryAltarNode>(3);
                    list.Add(n);
                }
        }
        return _children.TryGetValue(node.Id, out var found) ? found : (IReadOnlyList<MemoryAltarNode>)System.Array.Empty<MemoryAltarNode>();
    }

    /// <summary>
    /// 가운데 제단에서 몇 칸째인가(뿌리 = 1). 부모가 여럿이면 가장 깊은 부모 + 1.
    /// <para>이것이 트리의 <b>고리</b>이자 확장 페이즈다 — 1 정착 · 2 확장 · 3 심화 · 4 완성 · 5 심연.</para>
    /// </summary>
    public static int Depth(MemoryAltarNode node)
    {
        if (node == null) return 0;
        if (_depth == null) _depth = new Dictionary<string, int>(Nodes.Length);
        if (_depth.TryGetValue(node.Id, out var d)) return d;

        int best = 0;
        foreach (var p in node.Parents)
        {
            var parent = Get(p);
            if (parent != null) best = System.Math.Max(best, Depth(parent));
        }
        _depth[node.Id] = best + 1;
        return best + 1;
    }

    /// <summary>
    /// 아직 안 열린 부모 중 첫 번째(없으면 null) — 「앞 노드 먼저」 안내에 쓴다.
    /// </summary>
    public static MemoryAltarNode FirstLockedParent(MemoryAltarNode node, System.Func<string, bool> isUnlocked)
    {
        if (node == null || isUnlocked == null) return null;
        foreach (var p in node.Parents)
            if (!isUnlocked(p)) return Get(p);
        return null;
    }

    /// <summary>갈래 하나의 노드를 정의 순서대로.</summary>
    public static List<MemoryAltarNode> GetBranch(AltarBranch branch)
    {
        var list = new List<MemoryAltarNode>(8);
        foreach (var n in Nodes)
            if (n.Branch == branch) list.Add(n);
        return list;
    }

    /// <summary>
    /// 갈래 이름. <b>번호를 붙이지 않는다</b> — 「Ⅰ→Ⅱ→Ⅲ→Ⅳ」는 순서를 약속하는데
    /// 갈래 사이엔 순서가 없다(아무 데나 고른다). 순서가 있는 것은 갈래 <b>안쪽</b>이고, 그건 화면의 선이 말한다.
    /// </summary>
    public static string BranchLabel(AltarBranch branch) => branch switch
    {
        AltarBranch.Rune     => "룬",
        AltarBranch.Covenant => "서약",
        AltarBranch.Weapon   => "무기",
        AltarBranch.Journey  => "여정",
        AltarBranch.Memory   => "유물의 기억",
        _                    => "",
    };

    /// <summary>갈래 머리의 한 줄 물음 — 이 갈래에서 무엇이 넓어지는지를 말한다.</summary>
    public static string BranchQuestion(AltarBranch branch) => branch switch
    {
        AltarBranch.Rune     => "어떤 룬이 나오나",
        AltarBranch.Covenant => "어떤 서약을 맺나",
        AltarBranch.Weapon   => "무엇을 들고 싸우나",
        AltarBranch.Journey  => "얼마나 멀리 가나",
        AltarBranch.Memory   => "되찾은 기억",
        _                    => "",
    };

    /// <summary>
    /// 그 시기의 칸이 드러났는가 — <b>10-14 폐기: 늘 true</b>(사용자 「해금 내용이 봉인기 · 악몽기 등에 따라 늘어나는 것은 불편만 준다」).
    /// 처음부터 모든 칸이 보인다. 살 수 있는가는 갈래 포인트(고리) · 논리상 앞 칸 · 자물쇠 조건(심연 입장 = 엔딩)만 정한다.
    /// (10-02엔 1 봉인기 · 2 해방기(붕괴 뒤) · 3 악몽(엔딩 뒤)에 맞춰 고리가 드러났다.)
    /// </summary>
    public static bool IsEraRevealed(int era) => true;

    /// <summary>드러난 가장 늦은 시기 — 10-14부터 늘 3(전부).</summary>
    public static int RevealedEra => 3;
}
