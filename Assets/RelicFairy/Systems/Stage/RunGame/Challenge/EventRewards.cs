using UnityEngine;

/// <summary>
/// 이벤트방 특별 보상(10-29 사용자 「룬이 아닌 특별한 보상」 · 기획/RelicFairy_이벤트방_정리·특수보상_설계_20261029.md §3).
/// 이벤트가 끝나면 그 자리에서 바로 준다 — 룬 고르기 창(RoomClearGate)을 띄우지 않으므로 끝나는 순간 출구가 열린다.
/// 양 = 등급(실패 · 브론즈 · 실버 · 골드 · 플래티넘) × 챕터. 회복 · 물약은 주지 않는다(흡혈 · 회복 금지 정책).
///   · 금화 15 · 30 · 45 · 65 · 90 × 챕터 1 · 1.2 · 1.4 · 1.6 — 전투방 한 곳 금화(약 50 ~ 90)와 비슷한 선
///   · 정수 8 · 16 · 24 · 34 · 46 × 챕터 1 · 1.1 · 1.2 · 1.3 — 방 클리어 28 · 정예 보스 130 사이(AbyssEssenceTracker)
///   · 강화재료 6 · 14 · 25 · 39 · 56 × 챕터 1 · 1.15 · 1.2 · 1.2 — 전투 챌린지 표 그대로(ChallengeRewardTable, 상호작용 ×0.65 없음)
///   · 원석 3 · 6 · 9 · 12 · 16 × 강화재료와 같은 챕터 배율
/// 금화 · 정수는 발밑에 줍는 물건으로 떨어뜨린다(자석 거리 안 — 곧바로 들어온다). 강화재료 · 원석은 바로 은행에 넣는다(HUD 획득 목록이 알린다).
/// </summary>
public static class EventRewards
{
    // ── Constants ──────────────────────────────────────────────
    private static readonly int[]   GoldBase       = { 15, 30, 45, 65, 90 };
    private static readonly int[]   EssenceBase    = { 8, 16, 24, 34, 46 };
    private static readonly int[]   OreBase        = { 3, 6, 9, 12, 16 };
    private static readonly float[] GoldChapter    = { 1f, 1.2f, 1.4f, 1.6f };
    private static readonly float[] EssenceChapter = { 1f, 1.1f, 1.2f, 1.3f };
    private static readonly float[] FuelChapter    = { 1f, 1.15f, 1.2f, 1.2f };   // ChallengeRewardTable.DefaultChapterScale과 같다
    private const int CoinUnit = 6;    // 금화 한 닢 몫(대략) — 90이면 15닢
    private const int MaxCoins = 12;

    // ── Public Methods — 양 ────────────────────────────────────
    public static int Chapter(GameRunSession run) => Mathf.Clamp(run != null ? (int)run.CurrentChapter : 1, 1, 4);

    public static int Gold(ChallengeGrade g, int chapter)     => Mathf.RoundToInt(GoldBase[Grade(g)] * GoldChapter[Ch(chapter)]);
    public static int Essence(ChallengeGrade g, int chapter)  => Mathf.RoundToInt(EssenceBase[Grade(g)] * EssenceChapter[Ch(chapter)]);
    public static int Material(ChallengeGrade g, int chapter) => ChallengeRewardTable.DefaultReward(g, chapter, false).fuelAmount;
    public static int Ore(ChallengeGrade g, int chapter)      => Mathf.RoundToInt(OreBase[Grade(g)] * FuelChapter[Ch(chapter)]);

    // ── Public Methods — 주기 ──────────────────────────────────

    /// <summary>금화를 <paramref name="at"/>에 쏟는다. 마지막 한 닢이 나머지를 가져 총량이 정확히 남는다.</summary>
    public static void DropGold(Vector3 at, int amount)
    {
        if (amount <= 0) return;
        int coins = Mathf.Clamp(amount / CoinUnit, 1, MaxCoins);
        int per   = amount / coins;
        if (coins > 1) GoldCoinPickup.SpawnDrops(at, coins - 1, per);
        GoldCoinPickup.SpawnDrops(at, 1, amount - per * (coins - 1));
    }

    public static void DropEssence(Vector3 at, int amount) => EssenceShardPickup.SpawnDrops(at, amount);

    public static void AddMaterial(GameRunSession run, int amount) => run?.FuelBank?.Add(FuelKind.EnhanceMaterial, amount);
    public static void AddOre(GameRunSession run, int amount)      => run?.FuelBank?.Add(FuelKind.RuneOre, amount);

    /// <summary>근접 무기(무형검 · 카타나 · 대검)를 들고 있는가 — 기억 발판의 「증강 다시 뽑기」가 뜻이 있는가.</summary>
    public static bool HasMeleeWeapon(GameRunSession run)
    {
        var wm = run != null && run.Player != null ? run.Player.WeaponManager : null;
        if (wm == null) return false;
        return MeleeAugmentCatalog.KindOf(wm.Weapon0Data) != MeleeWeaponKind.None
            || MeleeAugmentCatalog.KindOf(wm.Weapon1Data) != MeleeWeaponKind.None;
    }

    /// <summary>플레이어 발밑(없으면 <paramref name="fallback"/>) — 떨어뜨린 금화 · 정수가 자석 거리 안에 든다.</summary>
    public static Vector3 PlayerPosOr(Vector3 fallback)
    {
        var pt = Managers.Player?.PlayerTransform;
        return pt != null ? pt.position : fallback;
    }

    public static void Notice(string msg)
    {
        var hud = Object.FindFirstObjectByType<HudPresenter>(FindObjectsInactive.Include);
        hud?.ShowBuffNotice(msg);
    }

    private const string RewardHex = "#FFD27A";   // 문 배지 둘째 줄의 보상 몫(금빛) — 「무엇을 받나」가 먼저 읽히게

    /// <summary>
    /// 이벤트방 문 배지 · 문 라벨 제목 — 이벤트 이름(10-29 사용자 「들어가기 전에 어떤 보상의 이벤트방인지」 · 예전 「이벤트」).
    /// 열쇠말은 SetupEventRoom과 같다. 제물 제단은 낼 대가가 없으면 신탁의 갈림길로 바뀌므로 지금 상태로 가른다.
    /// </summary>
    public static string Title(string poolKey)
    {
        string k = poolKey?.ToLowerInvariant() ?? string.Empty;
        var run = GameRunBootstrapper.Instance?.Run;
        if (k.Contains("barrage")) return "탄막의 회랑";
        if (k.Contains("memory"))  return "룬 기억";
        if (k.Contains("greed"))   return "욕심의 상자";
        if (k.Contains("sacrifice") && SacrificeAltarChallenge.CanPayAny(run)) return "제물 제단";
        if (k.Contains("sacrifice") || k.Contains("oracle")) return "신탁의 갈림길";
        if (k.Contains("vault") || k.Contains("treasure"))  return "보물 창고";
        if (k.Contains("sanctum")) return "서약 성소";
        if (k.Contains("gamble"))  return "도박 상자";
        return "전투 시험";   // 옛 전투형(빨리 · 안 맞기 · 버티기 …) — 방 풀 CDN 업로드 전 빌드에 남아 있다
    }

    /// <summary>
    /// 이벤트방 문 배지 둘째 줄 — <b>보상 먼저</b>(금빛) · 하는 일(10-29 「시험 · 선택」 → 「보상 정수 · 20초 피하기」).
    /// </summary>
    public static string Badge(string poolKey)
    {
        string k = poolKey?.ToLowerInvariant() ?? string.Empty;
        var run = GameRunBootstrapper.Instance?.Run;
        if (k.Contains("barrage")) return Line("정수", "20초 피하기");
        if (k.Contains("memory"))  return Line(HasMeleeWeapon(run) ? "증강 다시 뽑기" : "원석", "순서 외우기");
        if (k.Contains("greed"))   return Line("금화", "열다가 멈추기");
        if (k.Contains("sacrifice") && SacrificeAltarChallenge.CanPayAny(run)) return Line("유물 기억", "물약 · 금화를 바친다");
        if (k.Contains("sacrifice") || k.Contains("oracle")) return Line("강화재료 · 금화 · 정수", "하나를 고른다");
        if (k.Contains("vault") || k.Contains("treasure"))  return Line("금화 · 강화재료 · 정수", "15초 상자 열기");
        if (k.Contains("sanctum")) return Line("서약", "하나를 맺는다");
        if (k.Contains("gamble"))  return Line("룬", "상자 도박");
        return Line("룬 · 강화재료", "성적만큼");   // 옛 전투형 — 룬 고르기 + 강화재료(ChallengeRewardTable)
    }

    private static string Line(string reward, string doing) => $"보상 <color={RewardHex}>{reward}</color> · {doing}";

    // ── Private Methods ────────────────────────────────────────
    private static int Grade(ChallengeGrade g) => Mathf.Clamp((int)g, 0, 4);
    private static int Ch(int chapter)         => Mathf.Clamp(chapter, 1, 4) - 1;
}
