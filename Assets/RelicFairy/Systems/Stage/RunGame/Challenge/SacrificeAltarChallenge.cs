using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

/// <summary>
/// 제물 제단 — 비전투 이벤트방. <b>낼 수 있는 대가만</b> 보여 주고, 하나를 바치면 유물 기억 창(보스 보상과 같은 3지선다)을 한 번 연다
/// (10-29 개편 · 기획/RelicFairy_이벤트방_정리·특수보상_설계_20261029.md §3 · B1).
///   · 물약 1개 — 물약이 있을 때만 세운다(실제로 빠진 물약만 센다)
///   · 금화 N(챕터 60 · 80 · 100 · 120) — 그만큼 있을 때만 세운다
///   · 떠난다 — 아무것도 바치지 않고 지나간다(보상 없음)
/// 낼 것이 하나도 없으면 방을 세울 때 신탁의 갈림길로 바꾼다(GameRunBootstrapper.SetupEventRoom — 방 입장 상태로 정하니 이어하기도 같다).
/// 최대 체력은 대가로 받지 않는다 — 근접 버팀 방향과 부딪친다(10-29 사용자 결정).
/// 유물이 없거나 카드가 없으면 정수 · 금화(플래티넘 몫)로 대신한다.
/// </summary>
public sealed class SacrificeAltarChallenge : WorldInteractionChallenge
{
    private const string TagPotion = "potion";
    private const string TagGold   = "gold";
    private const string TagLeave  = "leave";
    private const string AltarProp   = "EventProp/Altar";   // 10-29 글자만 떠 있던 제단에 모델(고딕 제단)
    private const float  AltarHeight = 1.3f;
    private const float  AltarDist   = 3.4f;
    // 10-30 제단 셋 사이 각도 — ±38°(3.2 m)면 이웃 제단까지 2.1 m라 누르는 거리(2.8 m) 안에 둘이 들어와 엉뚱한 제단이 눌렸다(「떠난다」를 눌러 보상 없이 끝남)
    private const float  AltarYaw    = 60f;
    private const string RoomTitle   = "제물 제단";   // 10-30 이름표 · 안내 = 문 배지(EventRewards.Title)와 같은 이름(예전 챕터 테마 「이끼 낀 제단」 …)
    private const string RewardText  = "유물 기억 카드 고르기";

    private bool _paid;

    // ── Public Methods ────────────────────────────────────
    /// <summary>금화 대가 — 챕터 60 · 80 · 100 · 120.</summary>
    public static int GoldCost(GameRunSession run) => 40 + 20 * EventRewards.Chapter(run);

    public static bool CanPayPotion(GameRunSession run)
    {
        var ps = run?.PlayerState;
        return ps != null && ps.PotionCount > 0 && !ps.HealLocked;
    }

    public static bool CanPayGold(GameRunSession run)
    {
        var ps = run?.PlayerState;
        return ps != null && ps.TempGold >= GoldCost(run);
    }

    /// <summary>바칠 수 있는 것이 하나라도 있는가 — 없으면 이 방 대신 신탁의 갈림길(B1: 물약 0개로 들어오면 고를 게 없는 방이었다).</summary>
    public static bool CanPayAny(GameRunSession run) => CanPayPotion(run) || CanPayGold(run);

    /// <summary>SetupEventRoom이 호출.</summary>
    public void Initialize(GameRunSession run) => InitBase(run);

    // ── 서브클래스 훅 ─────────────────────────────────────
    /// <summary>10-30 O-02 — 남은 시간 아래 줄. 아무것도 안 내고 시간이 다 되면 받는 것이 없다.</summary>
    protected override string AutoResolveHint => "지나면 아무것도 받지 않고 출구가 열린다";

    protected override void PlaceMarkers(Transform player)
    {
        // 10-29 무대(방 가운데) 위 · 입구에서 바라보는 쪽 줄에 — 입구 4~5 m 앞에 세우면 이 방 가운데 띠의 안쪽 벽에 박혔다.
        var altarColor = new Color(0.95f, 0.55f, 0.45f);
        var ct = this.GetCancellationTokenOnDestroy();
        bool potion = CanPayPotion(Run), gold = CanPayGold(Run);
        int  cost   = GoldCost(Run);
        // 낼 수 있는 대가만 세운다 — 물약이 없으면 물약 칸이 안 선다
        // 10-30 「— 유물 기억」이 받는 것인지 붙은 이름인지 몰랐다 → 「내기 → 받는 것」으로
        if (potion)
        {
            Vector3 p = StageSpot(-AltarYaw, AltarDist);
            AddMarker(p, RoomTitle + " · 물약", altarColor, Loc.F("<color=#FF9668>[F]</color> 물약 1개 내기 → {0}", RewardText), TagPotion);
            SpawnPropAsync(AltarProp, p, FacingEntry(p), AltarHeight, ct).Forget();
        }
        if (gold)
        {
            Vector3 p = StageSpot(0f, AltarDist);
            AddMarker(p, RoomTitle + " · 금화", altarColor, Loc.F("<color=#FF9668>[F]</color> 금화 {0} 내기 → {1}", cost, RewardText), TagGold);
            SpawnPropAsync(AltarProp, p, FacingEntry(p), AltarHeight, ct).Forget();
        }
        AddMarker(StageSpot(AltarYaw, AltarDist), "떠난다", new Color(0.7f, 0.72f, 0.78f),
                  "<color=#B8C0CC>[F]</color> 그냥 지나친다", TagLeave);

        // 10-30 S-01 시작 알림 — 신탁에는 있는데 이 방엔 없었다. 낼 수 있는 대가만 적는다.
        string pay = potion && gold ? Loc.F("물약 1개나 금화 {0}을", cost) : potion ? "물약 1개를" : Loc.F("금화 {0}을", cost);
        Notice(Loc.F("{0} — {1} 내면 유물 기억 카드 1장을 고른다 · 안 내고 지나가도 된다", RoomTitle, pay));
    }

    protected override void OnActivate(int markerIndex)
    {
        var m = MarkerAt(markerIndex);
        if (m == null) return;
        string tag = (string)m.Tag;

        if (tag == TagLeave)
        {
            Notice("제단을 지나쳤다");
            SetLabel(markerIndex, "…", new Color(0.7f, 0.72f, 0.78f));
            FinishWith(ChallengeGrade.Fail, m.Pos);
            return;
        }

        // 하나만 바친다 — 실제로 빠졌을 때만 센다(TryConsumePotion · TrySpendGold가 false면 아무것도 바뀌지 않았다)
        var ps = Run?.PlayerState;
        int cost = GoldCost(Run);
        bool paid = ps != null && (tag == TagPotion ? ps.TryConsumePotion() : ps.TrySpendGold(cost));
        if (!paid)
        {
            Notice("<color=#B8C0CC>낼 수 없다</color>");
            return;
        }

        _paid = true;
        Managers.Sound?.PlayEvent(SoundEvent.SacrificeOffer);
        // 10-30 「기억이 떠오른다」는 비유 — 다음에 무엇이 뜨는지를 적는다
        Notice(tag == TagPotion ? "물약 1개를 냈다 — <color=#8fd3ff>유물 기억 카드를 1장 고른다</color>"
                                : Loc.F("금화 {0}을 냈다 — <color=#8fd3ff>유물 기억 카드를 1장 고른다</color>", cost));
        SetLabel(markerIndex, "봉헌!", new Color(1f, 0.75f, 0.4f));
        FinishWith(ChallengeGrade.Gold, m.Pos);
    }

    /// <summary>바쳤으면 유물 기억 창(보스 보상과 같은 3지선다)을 한 번 연다 — 고를 때까지 출구가 기다린다.</summary>
    protected override async UniTask GrantRewardAsync(ChallengeGrade grade, Vector3 center, CancellationToken ct)
    {
        if (!_paid) return;   // 지나쳤다 · 시간이 다 됐다 — 바친 것이 없으면 받는 것도 없다

        var boot = GameRunBootstrapper.Instance;
        if (boot != null && await boot.OfferRelicGiftAsync(ct)) return;

        // 유물 · 카드가 없다 — 플래티넘 몫 정수 두 배 + 금화로 대신한다
        int ch = EventRewards.Chapter(Run);
        int essence = EventRewards.Essence(ChallengeGrade.Platinum, ch) * 2;
        int gold    = EventRewards.Gold(ChallengeGrade.Platinum, ch);
        Vector3 at  = EventRewards.PlayerPosOr(center);
        EventRewards.DropEssence(at, essence);
        EventRewards.DropGold(at, gold);
        Notice(Loc.F("유물 기억 대신 — 정수 +{0} · 금화 +{1}", essence, gold));
    }
}
