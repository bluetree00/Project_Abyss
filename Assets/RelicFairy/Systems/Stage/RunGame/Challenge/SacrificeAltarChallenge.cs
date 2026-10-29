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
    private const float  AltarDist   = 3.2f;

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
    protected override void PlaceMarkers(Transform player)
    {
        // 10-29 무대(방 가운데) 위 · 입구에서 바라보는 쪽 줄에 — 입구 4~5 m 앞에 세우면 이 방 가운데 띠의 안쪽 벽에 박혔다.
        string altar = ChallengeFlavor.AltarName(Run);
        var altarColor = new Color(0.95f, 0.55f, 0.45f);
        var ct = this.GetCancellationTokenOnDestroy();
        // 낼 수 있는 대가만 세운다 — 물약이 없으면 물약 칸이 안 선다
        if (CanPayPotion(Run))
        {
            Vector3 p = StageSpot(-38f, AltarDist);
            AddMarker(p, altar + " · 물약", altarColor, "<color=#FF9668>[F]</color> 물약 1개를 바친다 — 유물 기억", TagPotion);
            SpawnPropAsync(AltarProp, p, FacingEntry(p), AltarHeight, ct).Forget();
        }
        if (CanPayGold(Run))
        {
            Vector3 p = StageSpot(0f, AltarDist);
            AddMarker(p, altar + " · 금화", altarColor, $"<color=#FF9668>[F]</color> 금화 {GoldCost(Run)}을 바친다 — 유물 기억", TagGold);
            SpawnPropAsync(AltarProp, p, FacingEntry(p), AltarHeight, ct).Forget();
        }
        AddMarker(StageSpot(38f, AltarDist), "떠난다", new Color(0.7f, 0.72f, 0.78f),
                  "<color=#B8C0CC>[F]</color> 그냥 지나친다", TagLeave);
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
            Notice("<color=#B8C0CC>바칠 수 없다</color>");
            return;
        }

        _paid = true;
        Managers.Sound?.PlayEvent(SoundEvent.SacrificeOffer);
        Notice(tag == TagPotion ? "물약 1개를 바쳤다 — <color=#8fd3ff>기억이 떠오른다</color>"
                                : $"금화 {cost}을 바쳤다 — <color=#8fd3ff>기억이 떠오른다</color>");
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
        Notice($"유물 기억 대신 — 정수 +{essence} · 금화 +{gold}");
    }
}
