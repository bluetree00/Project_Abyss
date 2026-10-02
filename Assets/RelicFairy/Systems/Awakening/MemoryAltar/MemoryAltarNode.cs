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
    /// 고리 = 시기(10-02 재설계). 1 봉인기(처음부터) · 2 해방기(첫 리치 = 붕괴 뒤 드러남) · 3 악몽(엔딩 뒤 드러남).
    /// 드러나지 않은 시기의 노드는 그리지도 팔지도 않는다(<see cref="MemoryAltarCatalog.IsEraRevealed"/>).
    /// </summary>
    public int Era { get; }

    public MemoryAltarNode(string id, AltarBranch branch, string displayName, string description,
                           int baseCost,
                           string conditionKey = null, int conditionTarget = 0,
                           string conditionLabel = null, int discountCost = 0,
                           bool conditionRequired = false,
                           string[] parents = null,
                           AltarNodeSize size = AltarNodeSize.Normal,
                           int era = 1)
    {
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
    /// <summary>「최대 체력 +76」 노드가 주는 값. <b>제단에 남은 유일한 영구 스탯</b>이고, 영구 공격력은 0이다.</summary>
    public const int MaxHpBonus = 76;

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
    public const string CovenantParts2 = "covenant_parts_2";
    public const string CovenantParts3 = "covenant_parts_3";

    public const string Revive         = "revive_once";
    public const string MaxHpUp        = "max_hp_up";

    public const string AbyssDepth     = "abyss_depth";
    public const string Chapter4       = "chapter_4";
    public const string DepthReward    = "depth_reward";

    // ── 판 넓히기(09-29 개편 신설) — 런의 칸 · 선택지를 넓힌다. 영구 스탯은 없다 ──
    public const string RuneStorage1   = "rune_storage_1";   // 룬 보관함 5 → 6
    public const string RuneStorage2   = "rune_storage_2";   // 룬 보관함 6 → 7
    public const string RefinePick     = "refine_pick";      // 정제 1개 → 2장 중 고르기
    public const string ShopReroll     = "shop_reroll";      // 상점 새로고침 개방
    public const string StartParts2    = "start_parts_2";    // 시작 파츠 1 → 2개
    public const string PotionSlot     = "potion_slot";      // 포션 3 → 4칸

    // ── 가운데 · 유물의 기억(10-02 재설계) — 유물 성장 「공명 그물」의 기억 카드를 넓힌다 ──
    public const string MemRedraw      = "mem_redraw";       // 런당 1회 기억 카드 3장 다시 굴리기
    public const string MemClear       = "mem_clear";        // 기억 카드 등급 띠 한 단
    public const string MemRadiant     = "mem_radiant";      // 등급 띠 한 단 더

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

    private static readonly MemoryAltarNode[] Nodes =
    {
        // 10-02 재설계 「기억을 모시는 제단」(설계서 `RelicFairy_기억의제단_재설계_설계서_20261002.md`).
        // 고리 = 시기: 1 봉인기(처음부터) · 2 해방기(붕괴 뒤) · 3 악몽(엔딩 뒤). 할인 조건 = 그 고리의 시기 기록.
        // 부모가 전부 열려야 산다(선으로 이어진 앞 노드). 가격은 정수 실측(챕터 완주 290~370)으로 시뮬레이션해 정했다 —
        // 봉인기 고리 18런(붕괴 22런 앞) · 해방기 31런(엔딩 32런 앞) · 전부 42런 · 챕터 4 개방 = 첫 완주 그 런.

        // ── 가운데 · 유물의 기억 ─────────────────────────
        new(MemRedraw,  AltarBranch.Memory, "다시 떠올리기", "런당 1회 — 보스를 쓰러뜨린 뒤 기억 카드 3장을 다시 굴린다", 1400,
            Rec.Seals, 2, "보스 2명 봉인", 900, size: AltarNodeSize.Keystone, era: 1),
        new(MemClear,   AltarBranch.Memory, "선명한 기억",   "기억 카드 등급 한 단 — 선명 · 찬란이 더 자주 뜬다", 2600,
            Rec.LibKills, 2, "해방된 보스 2회 처치", 1650,
            parents: new[] { MemRedraw }, size: AltarNodeSize.Keystone, era: 2),
        new(MemRadiant, AltarBranch.Memory, "찬란한 기억",   "기억 카드 등급 한 단 더 — 찬란이 더 자주 뜬다", 4500,
            Rec.NmKills, 2, "악몽 보스 2회 처치", 2800,
            parents: new[] { MemClear }, size: AltarNodeSize.Keystone, era: 3),

        // ── 룬 — 어떤 룬이 나오나 ─────────────────────────
        new(RuneStorage1,   AltarBranch.Rune, "룬 보관함 +1",   "룬 보관함  5칸 → 6칸",              700,
            Rec.RoomClears, 60, "방 60회 클리어", 450, size: AltarNodeSize.Small),
        new(RefinePick,     AltarBranch.Rune, "정제 두 장",     "정제할 때  룬 1개 → 2장 중 고르기", 1800,
            Rec.RefineCount, 10, "정제 10회", 1150,
            parents: new[] { RuneStorage1 }),
        new(RuneEpic,       AltarBranch.Rune, "영웅 룬 등장",   "룬 최고 등급  희귀 → 영웅",        2600,
            Rec.Seals, 3, "보스 3명 봉인", 1650,
            parents: new[] { RuneStorage1 }, size: AltarNodeSize.Keystone),
        new(RuneLegendary,  AltarBranch.Rune, "전설 룬 등장",   "룬 최고 등급  영웅 → 전설",        3000,
            Rec.LibKills, 3, "해방된 보스 3회 처치", 1900,
            parents: new[] { RuneEpic }, size: AltarNodeSize.Keystone, era: 2),
        // 영웅 룬 <b>뒤</b>여야 한다 — 영웅이 잠겨 있으면 정제소도 영웅을 낼 수 없어(등급 제한) 사 봐야 효과가 없다.
        new(RefineQuality,  AltarBranch.Rune, "정제 등급 상승", "정제소 영웅 확률  +12%p",          2700,
            Rec.RefineCount, 30, "정제 30회", 1700,
            parents: new[] { RuneEpic }, era: 2),
        new(RuneStorage2,   AltarBranch.Rune, "룬 보관함 +2",   "룬 보관함  6칸 → 7칸",              3000,
            Rec.NmKills, 3, "악몽 보스 3회 처치", 1900,
            parents: new[] { RefinePick }, size: AltarNodeSize.Small, era: 3),

        // ── 서약 — 어떤 서약을 맺나 ───────────────────────
        // 원인·효과 카드는 네 단계로 열린다(<see cref="CovenantPalette"/>의 개방 단계). 처음 5·6은 서약이
        // 무엇인지 가르치는 카드만 — 다른 서약과 맞물려야 빛나는 것(기폭·수확·정지·처형)은 뒤로 미룬다.
        new(CovenantParts1, AltarBranch.Covenant, "서약 카드 +5", "원인 5 → 8종 · 효과 7 → 9종",   1600,
            Rec.Covenants, 5, "서약 5번 맺기", 1000),
        // 「한 장의 서약서」(10-02) — id는 그대로(세이브), 뜻만 「서약 칸 +1」 → 「다섯째 절」
        new(CovenantSlot,   AltarBranch.Covenant, "다섯째 절",    "서약서 한 문장의 결과  4 → 5절", 2400,
            Rec.Covenants, 25, "서약 25번 맺기", 1500,
            parents: new[] { CovenantParts1 }, size: AltarNodeSize.Keystone, era: 2),
        new(CovenantParts2, AltarBranch.Covenant, "서약 카드 +4", "원인 8 → 9종 · 효과 9 → 12종",  2100,
            Rec.LibKills, 2, "해방된 보스 2회 처치", 1300,
            parents: new[] { CovenantParts1 }, era: 2),
        new(CovenantParts3, AltarBranch.Covenant, "서약 카드 +3", "효과 12 → 15종",                3200,
            Rec.NmKills, 2, "악몽 보스 2회 처치", 2000,
            parents: new[] { CovenantParts2 }, era: 3),

        // ── 무기 — 무엇을 들고 싸우나(근접 + 원거리) ─────────
        // 주무기는 항상 무형검이고(시나리오), 카타나·대검은 런 안의 진화 분기라 노드가 아니다.
        // 원거리 시작 파츠는 베이스캠프 파츠 작업대에서 매 런 Lv1로 들고 나간다 — 분열의 시위는 기본으로 열려 있다(사용자 결정).
        new(PartPierce,     AltarBranch.Weapon, "관통의 촉",        "시작 파츠  +관통",                450,
            Rec.RoomClears, 30, "방 30회 클리어", 300),
        new(WeaponCrossbow, AltarBranch.Weapon, "석궁",             "시작 원거리 무기  활 → 활·석궁",  900,
            Rec.EliteKills, 5, "정예 5회 처치", 600,
            parents: new[] { PartPierce }, size: AltarNodeSize.Keystone),
        new(PartPower,      AltarBranch.Weapon, "거력의 축",        "시작 파츠  +거력",                1200,
            Rec.EliteKills, 12, "정예 12회 처치", 800,
            parents: new[] { PartPierce }),
        new(SigilSmith,     AltarBranch.Weapon, "대장장이의 인장",  "재련 강화 성공률  +8%p",          1500,
            Rec.MaxEnhance, 6, "무기 +6 도달", 950),
        new(PartHoming,     AltarBranch.Weapon, "추적의 깃",        "시작 파츠  +추적",                1500,
            Rec.LibKills, 1, "해방된 보스 1회 처치", 950,
            parents: new[] { PartPower }, era: 2),
        // 시작 파츠가 셋(분열·관통·거력)은 있어야 「둘 들고 가기」가 고르는 일이 된다.
        new(StartParts2,    AltarBranch.Weapon, "시작 파츠 둘",     "시작 파츠  1개 → 2개 들고 가기",  2300,
            Rec.EliteKills, 40, "정예 40회 처치", 1450,
            parents: new[] { PartPower }, era: 2),
        // 승급 자체는 해금 없이도 된다(강화 MAX면 가능) — 해금이 넓히는 것은 <b>후보의 수</b>다.
        new(WeaponEvolve,   AltarBranch.Weapon, "전설 무기 3종",    "승급할 전설 무기  1종 → 3종",     2800,
            Rec.LibKills, 3, "해방된 보스 3회 처치", 1750,
            parents: new[] { SigilSmith }, size: AltarNodeSize.Keystone, era: 2),
        new(PartExplode,    AltarBranch.Weapon, "작렬의 탄두",      "시작 파츠  +작렬",                2600,
            Rec.NmKills, 1, "악몽 보스 1회 처치", 1650,
            parents: new[] { PartHoming }, era: 3),

        // ── 여정 — 얼마나 멀리 가나 ───────────────────────
        // 버티는 것(부활 · 체력 · 포션) · 길의 상점(인장 · 새로고침) · 더 멀리(챕터 4 · 심연). 진행 관문 앞에는 비싼 칸을 두지 않는다 —
        // 끼면 챕터 4가 몇 런씩 밀린다(그래서 챕터 4는 뿌리 · 800). 버티는 쪽은 조건 없음: 벽을 넘게 해주는 것이라 무조건 열려야 한다.
        new(Revive,         AltarBranch.Journey, "부활 1회",       "런당 1회 — 쓰러지면 최대 체력 50%로 일어나 2초 무적",   800,
            size: AltarNodeSize.Keystone),
        new(SigilMerchant,  AltarBranch.Journey, "상인의 인장",    "상점 가격  -15%",                 1300,
            Rec.ShopUses, 5, "상점 5회 이용", 850),   // 뿌리 — 사슬을 짧게(트리 깊이 8 → 6칸, 10-02 화면 실측: 이름표가 9px까지 작아졌다)
        new(ShopReroll,     AltarBranch.Journey, "상점 새로고침",  "상점마다 1회 · 10골드로 진열 새로고침",   1600,
            Rec.ShopUses, 10, "상점 10회 이용", 1000,
            parents: new[] { SigilMerchant }, size: AltarNodeSize.Small),
        // 이 둘만 조건이 <b>자물쇠</b>다 — 논리적 선후가 있어 데드락이 아니다(정본 §2-3).
        // 세 보스를 봉인(첫 완주)하면 성소의 문(챕터 4)이 열리고, 심연(순환)은 악몽기 리치를 쓰러뜨린 엔딩 뒤에 열린다.
        new(Chapter4,       AltarBranch.Journey, "챕터 4 개방",    "갈 수 있는 챕터  3 → 4",         800,
            Rec.Clears, 1, "첫 완주", 800, conditionRequired: true, size: AltarNodeSize.Keystone),
        new(MaxHpUp,        AltarBranch.Journey, "최대 체력 +76",  "최대 체력  +76",                 1400,
            parents: new[] { Revive }, era: 2),
        new(PotionSlot,     AltarBranch.Journey, "포션 칸 +1",     "포션  3칸 → 4칸",                1900,
            parents: new[] { MaxHpUp }, size: AltarNodeSize.Small, era: 2),
        new(AbyssDepth,     AltarBranch.Journey, "심연 입장",      "엔딩 뒤 심연 개방 — 깊이마다 적 체력 · 공격력 +25%",  1000,
            StoryProgress.Rec.Ending, 1, "성소의 주인을 완전히 쓰러뜨리기", 1000, conditionRequired: true,
            parents: new[] { Chapter4 }, size: AltarNodeSize.Keystone, era: 3),
        new(DepthReward,    AltarBranch.Journey, "깊이 보상",      "심연 깊이마다 정수  +15%",       3400,
            Rec.NmKills, 4, "악몽 보스 4회 처치", 2200,
            parents: new[] { AbyssDepth }, era: 3),
        new(SigilAscetic,   AltarBranch.Journey, "고행자의 인장",  "보상 선택지 -1 · 정수 ×1.6 (켜고 끄기)", 4200,
            Rec.NmKills, 8, "악몽 보스 8회 처치", 2700,
            parents: new[] { AbyssDepth }, size: AltarNodeSize.Keystone, era: 3),   // 깊이 보상과 형제 — 사슬을 짧게
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

    /// <summary>고리의 이름 = 시기(10-02 재설계 — 옛 「정착 · 확장 · 심화 · 완성 · 심연」 깊이 이름은 버렸다).</summary>
    public static string RingLabel(int era) => era switch
    {
        1 => "봉인기",
        2 => "해방기",
        _ => "악몽",
    };

    /// <summary>
    /// 그 시기의 고리가 드러났는가 — 1 봉인기는 처음부터, 2 해방기는 첫 리치(붕괴) 뒤, 3 악몽은 엔딩 뒤.
    /// 드러나지 않은 고리의 노드는 그리지도 팔지도 않는다(설계서 §4 — 붕괴 · 엔딩 스포일러 금지).
    /// </summary>
    public static bool IsEraRevealed(int era) =>
        era <= 1 || (era == 2 && StoryProgress.IsLiberated) || (era >= 3 && StoryProgress.HasEnded);

    /// <summary>지금 드러난 가장 늦은 시기(1~3).</summary>
    public static int RevealedEra => IsEraRevealed(3) ? 3 : IsEraRevealed(2) ? 2 : 1;
}
