using System.Collections.Generic;

/// <summary>기억의 제단 갈래 — 추상어가 아니라 플레이어가 실제로 갖는 질문으로 묶는다(정본 §3).</summary>
public enum AltarBranch
{
    /// <summary>Ⅰ 출발 — 무엇으로 시작하는가</summary>
    Start,
    /// <summary>Ⅱ 등장 — 무엇이 나올 수 있는가 (제단의 심장)</summary>
    Appear,
    /// <summary>Ⅲ 존속 — 얼마나 버틸 수 있는가</summary>
    Endure,
    /// <summary>Ⅳ 심연 — 얼마나 깊이 갈 수 있는가</summary>
    Abyss,
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
    /// 조건이 <b>자물쇠</b>인 예외 노드. 정본 §2-3의 둘(심연 깊이 개방·챕터4)만 true —
    /// 논리적 선후가 있어 데드락이 아니다. 나머지는 전부 false여야 한다.
    /// </summary>
    public bool ConditionRequired { get; }

    public MemoryAltarNode(string id, AltarBranch branch, string displayName, string description,
                           int baseCost,
                           string conditionKey = null, int conditionTarget = 0,
                           string conditionLabel = null, int discountCost = 0,
                           bool conditionRequired = false)
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
    }

    public bool HasCondition => !string.IsNullOrEmpty(ConditionKey);
}

/// <summary>
/// 기억의 제단이 파는 것 전량(21노드). 정본 §3 표를 그대로 옮긴 것.
/// <para><b>왜 CSV가 아니라 코드인가</b> — 차트로 빼면 CDN 스키마 변경이라 조율이 필요하다.
/// 초기엔 정적 등록이 빠르다(<c>CovenantPalette</c> 선례). 정본 §10-6의 열린 결정.</para>
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

        // ── 기행(自發 난이도) — 완주 전에 자발적으로 어렵게 가는 사람을 위한 축 ──
        public const string NoPotionClear   = "noPotionClear";   // 포션 0개로 완주(0/1)
        public const string NoSpecialClear  = "noSpecialClear";  // 특수방 0회로 완주(0/1)
        public const string FlawlessChapter = "flawlessChapter"; // 무피격 챕터 클리어 누적

        /// <summary>각성 6계열 정수 환급을 이미 수행했는가(1회성 마이그레이션 가드).</summary>
        public const string AwakeningRefunded = "awakeningRefunded";

        /// <summary>업적 진척으로 흘려보낼 기록 키 전량. 내부 가드(AwakeningRefunded)는 제외한다.</summary>
        public static readonly string[] All =
        {
            MaxDepth, MaxChapter, EliteKills, BossKills, Kills,
            RoomClears, MaxEnhance, ShopUses, RefineCount, Clears,
            NoPotionClear, NoSpecialClear, FlawlessChapter,
        };
    }

    // ── 노드 id ─────────────────────────────────────────
    public const string WeaponCrossbow = "weapon_crossbow";
    // ⚠️ 카타나·대검·활은 <b>노드가 아니다.</b>
    //   · 활  = 기본 지급 원거리 무기.
    //   · 카타나·대검 = 무형검(T0_Nameless)의 <b>런 중 진화 분기</b>(NamelessEvolution). 매 런 무료로 열려 있다.
    //     시나리오상 무형검은 "무엇이든 될 수 있는" 검이라 형(形)은 런 안에서 벼려야 하고,
    //     그것을 정수로 파는 순간 "이미 있는 걸 잠갔다 푸는 지연"이 된다.
    public const string PartsInherit   = "parts_inherit";
    public const string SigilMerchant  = "sigil_merchant";
    public const string SigilSmith     = "sigil_smith";
    public const string SigilAscetic   = "sigil_ascetic";

    public const string RuneChoice4    = "rune_choice_4";
    public const string PartsDraft4    = "parts_draft_4";
    public const string RefineQuality  = "refine_quality";
    public const string RuneEpic       = "rune_epic";
    public const string CorePartsTier1 = "core_parts_1";
    public const string RuneLegendary  = "rune_legendary";
    public const string CorePartsAll   = "core_parts_all";
    public const string WeaponEvolve   = "weapon_evolve";

    public const string Revive         = "revive_once";
    public const string MaxHpUp        = "max_hp_up";

    public const string AbyssDepth     = "abyss_depth";
    public const string Chapter4       = "chapter_4";
    public const string DepthReward    = "depth_reward";

    private static readonly MemoryAltarNode[] Nodes =
    {
        // ── Ⅰ 출발 (5) — 무엇으로 시작하는가 ──────────────
        // 이 게임에서 <b>런 시작 시점에 정해지는 것</b>은 원거리 무기·인장·계승뿐이다.
        // 주무기는 항상 무형검이고(시나리오), 그 형(形)은 런 안에서 재련소가 벼린다.
        new(WeaponCrossbow, AltarBranch.Start, "석궁",              "무기대에 석궁이 선택지로 선다",            2400,
            Rec.MaxEnhance, 6, "무기 +6 도달", 1600),
        new(PartsInherit,   AltarBranch.Start, "파츠 영구 계승",     "파츠 1개를 다음 런으로 가져간다",           2400,
            Rec.MaxChapter, 3, "3챕터 도달", 1600),
        new(SigilMerchant,  AltarBranch.Start, "상인의 인장",        "상점 가격이 낮아진 채로 시작한다",           2000,
            Rec.ShopUses, 5, "상점 5회 이용", 1400),
        new(SigilSmith,     AltarBranch.Start, "대장장이의 인장",    "무기 강화 성공률이 오른 채로 시작한다",       2000,
            Rec.MaxEnhance, 9, "무기 +9 도달", 1400),
        new(SigilAscetic,   AltarBranch.Start, "고행자의 인장",      "더 적은 보상 대신 더 큰 배율로 시작한다",     6000,
            Rec.MaxDepth, 6, "깊이 6 클리어", 3000),

        // ── Ⅱ 등장 (8) ★ 제단의 심장 ─────────────────────
        // 전부 "풀에 들어오되 매 런 다시 뽑아야 하는 것"이다 — 확률적이라 열려도 안 나올 수 있다.
        // ★ 이 노드가 <b>온램프</b>다 — 첫 챕터를 깨면 약 280정수가 들어오므로 400은 그 다음 런에 닿는다.
        // (정본의 「카타나 300」이 맡던 자리다. 카타나는 무형검 진화 분기라 노드에서 빠졌고,
        //  그 자리를 비워두면 최저가가 1,000이 되어 첫 해금이 8런째로 밀린다 — 시뮬레이션 실측.)
        new(RuneChoice4,    AltarBranch.Appear, "룬 4지선다",        "룬 선택지가 3개에서 4개로 늘어난다",          400,
            Rec.RoomClears, 50, "방 50회 클리어", 280),
        new(PartsDraft4,    AltarBranch.Appear, "파츠 드래프트 4",   "보스 파츠 선택지가 3개에서 4개로 늘어난다",   1200,
            Rec.BossKills, 3, "보스 3회 처치", 800),
        new(RefineQuality,  AltarBranch.Appear, "정제 품질",         "존핵 상위 등급 확률이 오른다",              1800,
            Rec.RefineCount, 30, "정제 30회", 1200),
        new(RuneEpic,       AltarBranch.Appear, "Epic 룬 개방",      "Epic 등급이 드랍 풀에 들어온다 (값 ×1.5)",   4000,
            Rec.MaxDepth, 1, "깊이 1 클리어", 2000),
        new(CorePartsTier1, AltarBranch.Appear, "코어 파츠 1차",     "유물 메커닉이 바뀌는 코어 파츠가 등장한다",   5200,
            Rec.MaxDepth, 2, "깊이 2 클리어", 2600),
        new(RuneLegendary,  AltarBranch.Appear, "Legendary 룬 개방", "Legendary 등급이 드랍 풀에 들어온다 (값 ×2.2)", 6400,
            Rec.MaxDepth, 3, "깊이 3 클리어", 3200),
        new(CorePartsAll,   AltarBranch.Appear, "코어 파츠 전체",    "유물당 코어 파츠 3종이 전부 등장한다",       7600,
            Rec.MaxDepth, 4, "깊이 4 클리어", 3800),
        // 승급 자체는 해금 없이도 된다(강화 MAX면 가능) — 해금이 넓히는 것은 <b>후보의 수</b>다.
        // 미해금이면 엑스칼리버 하나로 고정되고, 열면 갈라틴·아론다이트까지 셋 중에 고른다.
        new(WeaponEvolve,   AltarBranch.Appear, "전설 3종 개방",     "승급 시 전설 후보가 1종에서 3종으로 늘어난다", 8000,
            Rec.MaxDepth, 5, "깊이 5 클리어", 4000),

        // ── Ⅲ 존속 (2) — 조건 없음 ───────────────────────
        // 벽을 넘게 해주는 것이라 무조건 열려야 한다. 영구 공격력은 0이다.
        new(Revive,  AltarBranch.Endure, "부활 1회",      "런당 1회 부활한다",        1200),
        new(MaxHpUp, AltarBranch.Endure, "최대 체력 +76", "최대 체력이 76 오른다",    1500),

        // ── Ⅳ 심연 (3) ──────────────────────────────────
        // 앞의 둘만 조건이 <b>자물쇠</b>다 — 논리적 선후가 있어 데드락이 아니다(정본 §2-3).
        new(AbyssDepth,  AltarBranch.Abyss, "심연 깊이 개방", "완주 후 더 깊이 이어갈 수 있다", 1000,
            Rec.Clears, 1, "첫 완주", 1000, conditionRequired: true),
        new(Chapter4,    AltarBranch.Abyss, "챕터 4 개방",   "4챕터로 나아갈 수 있다",         1800,
            Rec.MaxDepth, 1, "깊이 1 도달", 1800, conditionRequired: true),
        new(DepthReward, AltarBranch.Abyss, "깊이 보상 배율", "깊이마다 보상이 15% 늘어난다",   3000,
            Rec.MaxDepth, 2, "깊이 2 클리어", 2200),
    };

    private static Dictionary<string, MemoryAltarNode> _byId;

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

    /// <summary>갈래 하나의 노드를 정의 순서대로 — 화면의 열 순서가 곧 이 순서다.</summary>
    public static List<MemoryAltarNode> GetBranch(AltarBranch branch)
    {
        var list = new List<MemoryAltarNode>(8);
        foreach (var n in Nodes)
            if (n.Branch == branch) list.Add(n);
        return list;
    }

    public static string BranchLabel(AltarBranch branch) => branch switch
    {
        AltarBranch.Start  => "Ⅰ 출발",
        AltarBranch.Appear => "Ⅱ 등장",
        AltarBranch.Endure => "Ⅲ 존속",
        AltarBranch.Abyss  => "Ⅳ 심연",
        _                  => "",
    };
}
