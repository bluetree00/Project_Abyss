using System.Collections.Generic;

/// <summary>
/// 기억의 제단 갈래 — <b>무엇이 넓어지는가</b>(시스템)로 묶는다. 이름만 보고 무엇이 열리는지 알 수 있어야 한다.
/// <para>2026-09-16 재정렬 — 예전 갈래(출발·등장·존속·심연)는 질문으로 묶여 룬 해금이 두 갈래에 흩어져 있었다.
/// 값(0~3)은 저장되지 않는다(노드 정의가 코드에 있다).</para>
/// </summary>
public enum AltarBranch
{
    /// <summary>룬 — 어떤 룬이 나오나</summary>
    Rune,
    /// <summary>서약 — 어떤 서약을 맺나</summary>
    Covenant,
    /// <summary>장비 — 무엇을 들고 가나</summary>
    Gear,
    /// <summary>여정 — 얼마나 멀리 가나</summary>
    Journey,
    /// <summary>원거리 — 무엇을 쏘나(석궁 + 시작 파츠 4종, 09-27 신설 — 장비 열이 12칸이 되어 화면에 안 들어갔다)</summary>
    Ranged,
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
/// 기억의 제단이 파는 것 전량(26노드 · 갈래 5 — 룬 4 · 서약 4 · 장비 7 · 원거리 5 · 여정 6).
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
        public const string Covenants   = "covenants";    // 서약 맺은 수 누적(런 종료 시 그 런의 보유 수)

        // ── 기행(自發 난이도) — 완주 전에 자발적으로 어렵게 가는 사람을 위한 축 ──
        public const string NoPotionClear   = "noPotionClear";   // 포션 0개로 완주(0/1)
        public const string NoSpecialClear  = "noSpecialClear";  // 특수방 0회로 완주(0/1)
        public const string FlawlessChapter = "flawlessChapter"; // 무피격 챕터 클리어 누적

        /// <summary>각성 6계열 정수 환급을 이미 수행했는가(1회성 마이그레이션 가드).</summary>
        public const string AwakeningRefunded = "awakeningRefunded";

        /// <summary>무형검을 한 번 받았는가(0/1) — 베이스캠프 재설계: 검은 처음 한 번만 소환의 방에서 받고,
        /// 이후엔 베이스캠프가 슬롯0에 자동으로 쥐여 준다. 업적 진척이 아니므로 <see cref="All"/>에 넣지 않는다.</summary>
        public const string SwordAwakened = "swordAwakened";

        /// <summary>업적 진척으로 흘려보낼 기록 키 전량. 내부 가드(AwakeningRefunded)는 제외한다.</summary>
        public static readonly string[] All =
        {
            MaxDepth, MaxChapter, EliteKills, BossKills, Kills,
            RoomClears, MaxEnhance, ShopUses, RefineCount, Clears, Covenants,
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

    public const string RuneChoice4    = "rune_choice_4";
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

    private static readonly MemoryAltarNode[] Nodes =
    {
        // 갈래 = <b>무엇이 넓어지는가</b>(시스템)다. 예전 갈래(출발·등장·존속·심연)는 플레이어의 질문으로 묶었지만
        // 이름만 보고는 무엇이 열리는지 알 수 없었다 — 룬은 「출발」과 「등장」에 흩어져 있었다(2026-09-16 재정렬).
        //
        // ★ 순서 = 사슬이다. 위에서 아래로 <b>값과 조건 난이도가 함께 오른다</b>.
        //   순서는 확장 페이즈(A 정착 1~8런 · B 확장 9~16 · C 심화 17~22 · D 완성 23~34)를 따른다.
        //   가격은 정수 수급 곡선(런당 500 → 850 → 1,050 → 1,400 → 1,500)에 맞춰 시뮬레이션으로 정했다 —
        //   할인가 합 39,450 · 심연 입장 17런 · 챕터 4 개방 23~24런 · 전부 해금 약 38런
        //   (설계서 「시스템별 확장설계」 · 「해금체계 점진개방」).
        //   [09-28] 원거리 갈래(할인가 합 4,420)를 더하면 같은 모델로 전부 해금 38 → 41런(챕터 4 21런 · 심연 24런 — 09-17 이야기 순서 반영).
        //   정수 수급 +10%로 38런으로 되돌렸다(09-28 사용자 결정 — EssenceTracker 처치 9 · 방 16 · 보스 132, 설계서 §2-1).
        //   속성 존 5·6번째(다음 작업)가 룬 갈래에 들어오면 할인가 합은 약 43,250, 전부 해금은 약 40런이 된다.

        // ── 룬 — 어떤 룬이 나오나 ─────────────────────────
        new(RuneChoice4,    AltarBranch.Rune, "룬 선택지 +1",   "룬을 고를 때  3장 → 4장",          700,
            Rec.RoomClears, 50, "방 50회 클리어", 450),
        new(RuneEpic,       AltarBranch.Rune, "영웅 룬 등장",   "룬 최고 등급  희귀 → 영웅",        3000,
            Rec.Clears, 1, "첫 완주", 1900),
        // 영웅 룬 <b>뒤</b>여야 한다 — 영웅이 잠겨 있으면 정제소도 영웅을 낼 수 없어(등급 제한) 사 봐야 효과가 없다.
        new(RefineQuality,  AltarBranch.Rune, "정제 등급 상승", "정제소 영웅 확률  +12%p",          3200,
            Rec.RefineCount, 30, "정제 30회", 2000),
        new(RuneLegendary,  AltarBranch.Rune, "전설 룬 등장",   "룬 최고 등급  영웅 → 전설",        4500,
            Rec.MaxDepth, 2, "심연 깊이 2 도달", 2800),

        // ── 서약 — 어떤 서약을 맺나 ───────────────────────
        // 원인·효과 카드는 네 단계로 열린다(<see cref="CovenantPalette"/>의 개방 단계). 처음 5·6은 서약이
        // 무엇인지 가르치는 카드만 — 다른 서약과 맞물려야 빛나는 것(기폭·수확·정지·처형)은 뒤로 미룬다.
        new(CovenantParts1, AltarBranch.Covenant, "서약 카드 +5", "원인 5 → 8종 · 효과 7 → 9종",   2100,
            Rec.Covenants, 5, "서약 5번 맺기", 1300),
        new(CovenantSlot,   AltarBranch.Covenant, "서약 칸 +1",   "한 런에 맺는 서약  3 → 4개",    2700,
            Rec.Covenants, 15, "서약 15번 맺기", 1700),
        new(CovenantParts2, AltarBranch.Covenant, "서약 카드 +4", "원인 8 → 9종 · 효과 9 → 12종",  3000,
            Rec.Clears, 1, "첫 완주", 1800),
        new(CovenantParts3, AltarBranch.Covenant, "서약 카드 +3", "효과 12 → 15종",                4000,
            Rec.MaxDepth, 1, "심연 깊이 1 도달", 2500),

        // ── 장비 — 무엇을 들고 가나 ───────────────────────
        // 런 시작에 들고 나가는 것(원거리 무기·인장·계승)과 보스가 주는 유물 파츠, 재련소가 벼리는 전설 무기.
        // 주무기는 항상 무형검이고(시나리오), 카타나·대검은 런 안의 진화 분기라 노드가 아니다.
        new(SigilMerchant,  AltarBranch.Gear, "상인의 인장",        "상점 가격  -15%",                 1400,
            Rec.ShopUses, 5, "상점 5회 이용", 900),
        new(SigilSmith,     AltarBranch.Gear, "대장장이의 인장",    "재련 강화 성공률  +8%p",          1900,
            Rec.MaxEnhance, 6, "무기 +6 도달", 1200),
        // 09-27 감사: 유물마다 기능 파츠가 4개라 둘째 드래프트·이어받기 뒤엔 3장뿐이다 — 설명이 그 한계를 말한다.
        new(PartsDraft4,    AltarBranch.Gear, "보스 파츠 선택지 +1", "보스 파츠를 고를 때  3장 → 4장 (남은 파츠가 있을 때)", 2200,
            Rec.BossKills, 3, "보스 3회 처치", 1400),
        new(CorePartsTier1, AltarBranch.Gear, "코어 파츠 2종",      "코어 파츠 후보  1종 → 2종",       3000,
            Rec.MaxChapter, 3, "챕터 3 도달", 1900),
        // 승급 자체는 해금 없이도 된다(강화 MAX면 가능) — 해금이 넓히는 것은 <b>후보의 수</b>다.
        // 미해금이면 엑스칼리버 하나로 고정되고, 열면 갈라틴·아론다이트까지 셋 중에 고른다.
        new(WeaponEvolve,   AltarBranch.Gear, "전설 무기 3종",      "승급할 전설 무기  1종 → 3종",     3800,
            Rec.MaxDepth, 1, "심연 깊이 1 도달", 2400),
        // 09-27 감사: 선행 파츠가 필요한 코어(랜슬롯 「피의 만찬」 ← 출혈 낙인)는 그 파츠를 가진 뒤에야 후보에 든다.
        new(CorePartsAll,   AltarBranch.Gear, "코어 파츠 3종",      "코어 파츠 후보  2종 → 3종 (선행 파츠가 필요한 코어는 그 뒤에)", 4200,
            Rec.MaxDepth, 2, "심연 깊이 2 도달", 2600),
        new(PartsInherit,   AltarBranch.Gear, "파츠 이어받기",      "다음 런에 파츠 1개를 가져간다",   4800,
            Rec.MaxDepth, 4, "심연 깊이 4 도달", 3000),

        // ── 원거리 — 무엇을 쏘나 (09-27 신설) ─────────────
        // 석궁(장비에서 이동) + 시작 파츠 4종. 파츠는 베이스캠프 파츠 작업대에서 매 런 하나를 Lv1로 들고 나간다 —
        // 분열의 시위는 기본으로 열려 있다(사용자 결정). 사슬 순서 = 값·조건 오름차순.
        // [09-28 수급 시뮬] 확장 페이즈마다 하나씩: 관통 1런 · 석궁 3런(A 정착의 「시작 선택지」 — 확장설계 §1) · 거력 8런 ·
        //   추적 11런(B) · 작렬 18런(C, 첫 완주). 예전 값(관통·거력·석궁 정예30·추적 정예15·작렬 보스2)은 석궁·추적·작렬이 9~13런에
        //   몰려 B의 서약 카드를 15런까지 밀었고, 조건이 사슬 순서와 거꾸로였다(석궁 30 뒤에 추적 15). 설계서 §2-1.
        new(PartPierce,     AltarBranch.Ranged, "관통의 촉",        "시작 파츠  +관통",                500,
            Rec.RoomClears, 30, "방 30회 클리어", 320),
        new(WeaponCrossbow, AltarBranch.Ranged, "석궁",             "시작 원거리 무기  활 → 활·석궁",  900,
            Rec.EliteKills, 5, "정예 5회 처치", 600),
        new(PartPower,      AltarBranch.Ranged, "거력의 축",        "시작 파츠  +거력",                1400,
            Rec.EliteKills, 15, "정예 15회 처치", 900),
        new(PartHoming,     AltarBranch.Ranged, "추적의 깃",        "시작 파츠  +추적",                1700,
            Rec.EliteKills, 30, "정예 30회 처치", 1100),
        new(PartExplode,    AltarBranch.Ranged, "작렬의 탄두",      "시작 파츠  +작렬",                2400,
            Rec.Clears, 1, "첫 완주", 1500),

        // ── 여정 — 얼마나 멀리 가나 ───────────────────────
        // 버티는 것(부활·체력)이 먼저, 더 깊이 가는 것(심연·챕터 4)이 뒤다. 사슬이라 심연 앞에는
        // 싸고 이른 생존 노드만 둔다 — 진행 관문 앞에 비싼 칸이 끼면 챕터 4가 몇 런씩 밀린다.
        // 부활·체력은 조건 없음: 벽을 넘게 해주는 것이라 무조건 열려야 한다. 영구 공격력은 0이다.
        new(Revive,       AltarBranch.Journey, "부활 1회",       "쓰러져도 한 번 다시 일어선다",   1000),
        new(MaxHpUp,      AltarBranch.Journey, "최대 체력 +76",  "최대 체력  +76",                 1300),
        // 이 둘만 조건이 <b>자물쇠</b>다 — 논리적 선후가 있어 데드락이 아니다(정본 §2-3).
        // [09-17 재배치] 이야기 순서와 맞춘다 — 세 보스를 봉인(첫 완주)하면 성소의 문(챕터 4)이 열리고,
        // 심연(순환)은 악몽기 리치를 쓰러뜨린 엔딩 뒤에 열린다. (기획 「최종장이후_사이클시나리오」 v2 §3-2)
        new(Chapter4,     AltarBranch.Journey, "챕터 4 개방",    "갈 수 있는 챕터  3 → 4",         1800,
            Rec.Clears, 1, "첫 완주", 1800, conditionRequired: true),
        new(AbyssDepth,   AltarBranch.Journey, "심연 입장",      "엔딩 뒤 — 더 깊은 회차에 도전",  1000,
            StoryProgress.Rec.Ending, 1, "성소의 주인을 완전히 쓰러뜨리기", 1000, conditionRequired: true),
        new(DepthReward,  AltarBranch.Journey, "깊이 보상",      "심연 깊이마다 정수  +15%",       4200,
            Rec.MaxDepth, 2, "심연 깊이 2 도달", 2600),
        new(SigilAscetic, AltarBranch.Journey, "고행자의 인장",  "보상 선택지 -1 · 정수 ×1.6 (켜고 끄기)", 5100,
            Rec.MaxDepth, 6, "심연 깊이 6 도달", 3200),
    };

    private static Dictionary<string, MemoryAltarNode> _byId;
    private static Dictionary<string, MemoryAltarNode> _prevInBranch;

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
    /// 같은 갈래에서 <b>바로 앞</b> 노드. 갈래의 첫 노드면 null.
    /// <para>이것이 사슬이다 — 앞을 열어야 다음을 살 수 있다. <b>기록 자물쇠가 아니다</b>:
    /// 앞 칸은 정수만으로 항상 넘을 수 있고 방향이 하나뿐이라 순환(데드락)이 생길 수 없다.
    /// 갈래가 넷이라 언제나 최대 네 칸이 동시에 열려 있어 선택도 살아 있다.</para>
    /// </summary>
    public static MemoryAltarNode PreviousInBranch(MemoryAltarNode node)
    {
        if (node == null) return null;

        if (_prevInBranch == null)
        {
            _prevInBranch = new Dictionary<string, MemoryAltarNode>(Nodes.Length);
            var last = new Dictionary<AltarBranch, MemoryAltarNode>();
            foreach (var n in Nodes)
            {
                _prevInBranch[n.Id] = last.TryGetValue(n.Branch, out var p) ? p : null;
                last[n.Branch] = n;
            }
        }
        return _prevInBranch.TryGetValue(node.Id, out var prev) ? prev : null;
    }

    /// <summary>갈래 하나의 노드를 정의 순서대로 — 화면의 열 순서가 곧 사슬 순서다.</summary>
    public static List<MemoryAltarNode> GetBranch(AltarBranch branch)
    {
        var list = new List<MemoryAltarNode>(8);
        foreach (var n in Nodes)
            if (n.Branch == branch) list.Add(n);
        return list;
    }

    /// <summary>
    /// 갈래 이름. <b>번호를 붙이지 않는다</b> — 「Ⅰ→Ⅱ→Ⅲ→Ⅳ」는 순서를 약속하는데
    /// 갈래 사이엔 순서가 없다(넷 중 아무 데나 고른다). 순서가 있는 것은 갈래 <b>안쪽</b>이고,
    /// 그건 화면의 사슬 레일이 말한다. 진척은 열 머리의 n/N이 따로 보여준다.
    /// </summary>
    public static string BranchLabel(AltarBranch branch) => branch switch
    {
        AltarBranch.Rune     => "룬",
        AltarBranch.Covenant => "서약",
        AltarBranch.Gear     => "장비",
        AltarBranch.Journey  => "여정",
        AltarBranch.Ranged   => "원거리",
        _                    => "",
    };

    /// <summary>갈래 머리의 한 줄 물음 — 이 열에서 무엇이 넓어지는지를 말한다.</summary>
    public static string BranchQuestion(AltarBranch branch) => branch switch
    {
        AltarBranch.Rune     => "어떤 룬이 나오나",
        AltarBranch.Covenant => "어떤 서약을 맺나",
        AltarBranch.Gear     => "무엇을 들고 가나",
        AltarBranch.Journey  => "얼마나 멀리 가나",
        AltarBranch.Ranged   => "무엇을 쏘나",
        _                    => "",
    };
}
