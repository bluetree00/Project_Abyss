using UnityEngine;

namespace RelicFairy.Monster
{
/// <summary>
/// 리치 보스 전용 블랙보드.
/// BossAttackBlackboard를 상속받아 리치 전용 상태(페이즈, 패턴 쿨다운)를 추가한다.
/// 패턴에서는 ctx.Blackboard를 LichBlackboard로 캐스팅하여 전용 필드에 접근한다.
/// </summary>
public class LichBlackboard : BossAttackBlackboard
{
    // 속박탄(악몽 모드 특성 · 10-02) — 사슬 포박과 나누는 쿨다운 · 바닥 흔들림을 보는 시간
    public const float  BindBoltAfterCapture    = 6f;     // 사슬 포박이 끝난 뒤 이만큼 속박탄 없음
    public const float  CaptureAfterBindBolt    = 4f;     // 속박탄 결박 뒤 이만큼 사슬 포박 시작 없음
    private const float NeverAt                 = -999f;
    private const float CollapseUnstableSeconds = 4.5f;   // 영구 붕괴 — 전투 중 가장 긴 예고(R7 붕괴 마법 2.5초) + 낙하(1.8초)
    private const float BreakUnstableSeconds    = 2f;     // 일시 파괴 — 가장 긴 예고(M5p 순간이동 강타 1.0초) + 가라앉음(0.45초)

    private int _seenCollapsed = -1;
    private int _seenBroken    = -1;

    /// <summary>현재 페이지(1부터). 전환 패턴이 끝나는 순간 올라간다.</summary>
    public int Page { get; private set; } = 1;

    /// <summary>
    /// 이번 전투가 악몽기(해방된 리치)인가. 전투 시작 때 정하고, 전투 도중 붕괴가 일어나도 바꾸지 않는다.
    /// false = 봉인기(봉인된 리치).
    /// </summary>
    public bool IsNightmare { get; private set; }

    /// <summary>낫을 든 페이지(2 이상) — 낫 계열 패턴의 게이트. 봉인기 P2′·악몽기 P2·P3 공통.</summary>
    public bool IsPhase2 => Page >= 2;

    /// <summary>페이지 이동 속도 배율 — 전환 에셋의 speedMultiplier. LichMovementController가 이동에 곱한다(10-01 — 그동안 읽는 곳이 없었다).</summary>
    public float MoveSpeedMult = 1f;
    /// <summary>쉬는 시간 배율 — 악몽 모드 ×0.8(다른 보스와 같은 BossPages.NightmareBreakScale). 전투 시작 때 정한다.</summary>
    public float BreakScale = 1f;

    // ── 패턴 쿨다운 ───────────────────────────────────────
    public float MagicBoltCooldown;
    public float TeleportStrikeCooldown;
    public float ElementalBarrageCooldown;
    public float ArcaneOrbCooldown;
    public float ScytheSweepCooldown;
    public float ScytheThrowCooldown;
    public float BlinkStrikeCooldown;
    public float SkeletonSummonCooldown;
    public float SealBreakerCooldown;
    public float DarkRainCooldown;
    public float ReaperCadenceCooldown;
    public float ArcaneTorrentCooldown;
    public float TwinCastCooldown;
    public float ElementalRealignCooldown;
    public float ChainBreakCooldown;
    public float ReaperFlurryCooldown;
    public float ChainCaptureCooldown;
    public float AbyssalRendCooldown;
    public float FallingStarsCooldown;
    public float ScytheMagicCooldown;
    public float SealShardsCooldown;
    public float SealArrayCooldown;
    public float SkyDebrisCooldown;
    public float SoulCopyCooldown;
    public float InvertedSealCooldown;
    /// <summary>원소 재편(M8)을 이번 전투에 쓴 횟수 — 최대 횟수 제한용.</summary>
    public int   ElementalRealignUses;
    /// <summary>이번 전투에 강한 패턴(C8 등)이 영구히 무너뜨린 칸 수 — 필드를 갈아먹는 한계(09-19).</summary>
    public int   ErodedCells;
    /// <summary>최후의 대마법(F4)을 막았다 — 그 뒤로는 HP가 끝까지 깎인다.</summary>
    public bool  FinalMagicDone;

    // ── 속박탄(악몽 모드 특성) ────────────────────────────
    /// <summary>사슬 포박(C7)이 마지막으로 끝난 시각(Time.time) — 그 뒤 <see cref="BindBoltAfterCapture"/>초 동안 속박탄이 나오지 않는다.</summary>
    public float LastChainCaptureAt = NeverAt;
    /// <summary>속박탄 결박이 마지막으로 걸린 시각(Time.time) — 그 뒤 <see cref="CaptureAfterBindBolt"/>초 동안 사슬 포박이 시작하지 않는다.</summary>
    public float LastBindBoltAt     = NeverAt;
    /// <summary>이번 전투에 속박탄 안내 한 줄을 띄웠다.</summary>
    public bool  BindBoltHinted;
    /// <summary>바닥이 흔들리거나(붕괴 예고) 떨어지는 중으로 보는 남은 시간 — 묶인 채 낙사하지 않게 속박탄을 막는다.</summary>
    public float FloorUnstableTimer;
    public bool  FloorUnstable => FloorUnstableTimer > 0f;

    // ── 이동 컨트롤러용 ──────────────────────────────────
    /// <summary>LichMovementController가 매 Tick 갱신. 패턴 조건에서도 참조 가능.</summary>
    public float DistanceToPlayer;

    public new void TickCooldowns(float dt)
    {
        base.TickCooldowns(dt);
        if (MagicBoltCooldown        > 0f) MagicBoltCooldown        -= dt;
        if (TeleportStrikeCooldown   > 0f) TeleportStrikeCooldown   -= dt;
        if (ElementalBarrageCooldown > 0f) ElementalBarrageCooldown -= dt;
        if (ArcaneOrbCooldown        > 0f) ArcaneOrbCooldown        -= dt;
        if (ScytheSweepCooldown      > 0f) ScytheSweepCooldown      -= dt;
        if (ScytheThrowCooldown      > 0f) ScytheThrowCooldown      -= dt;
        if (BlinkStrikeCooldown      > 0f) BlinkStrikeCooldown      -= dt;
        if (SkeletonSummonCooldown   > 0f) SkeletonSummonCooldown   -= dt;
        if (SealBreakerCooldown      > 0f) SealBreakerCooldown      -= dt;
        if (DarkRainCooldown         > 0f) DarkRainCooldown         -= dt;
        if (ReaperCadenceCooldown    > 0f) ReaperCadenceCooldown    -= dt;
        if (ArcaneTorrentCooldown    > 0f) ArcaneTorrentCooldown    -= dt;
        if (TwinCastCooldown         > 0f) TwinCastCooldown         -= dt;
        if (ElementalRealignCooldown > 0f) ElementalRealignCooldown -= dt;
        if (ChainBreakCooldown       > 0f) ChainBreakCooldown       -= dt;
        if (ReaperFlurryCooldown     > 0f) ReaperFlurryCooldown     -= dt;
        if (ChainCaptureCooldown     > 0f) ChainCaptureCooldown     -= dt;
        if (AbyssalRendCooldown      > 0f) AbyssalRendCooldown      -= dt;
        if (FallingStarsCooldown     > 0f) FallingStarsCooldown     -= dt;
        if (ScytheMagicCooldown      > 0f) ScytheMagicCooldown      -= dt;
        if (SealShardsCooldown       > 0f) SealShardsCooldown       -= dt;
        if (SealArrayCooldown        > 0f) SealArrayCooldown        -= dt;
        if (SkyDebrisCooldown        > 0f) SkyDebrisCooldown        -= dt;
        if (SoulCopyCooldown         > 0f) SoulCopyCooldown         -= dt;
        if (InvertedSealCooldown     > 0f) InvertedSealCooldown     -= dt;
        if (FloorUnstableTimer       > 0f) FloorUnstableTimer       -= dt;
        TrackFloor();
    }

    public void SetPage(int page) => Page = page;

    public void SetMode(bool nightmare) => IsNightmare = nightmare;

    public new void Reset()
    {
        base.Reset();
        Page                     = 1;   // 모드는 LichMonster가 전투마다 다시 정한다
        MoveSpeedMult            = 1f;
        BreakScale               = 1f;
        MagicBoltCooldown        = 0f;
        TeleportStrikeCooldown   = 0f;
        ElementalBarrageCooldown = 0f;
        ArcaneOrbCooldown        = 0f;
        ScytheSweepCooldown      = 0f;
        ScytheThrowCooldown      = 0f;
        BlinkStrikeCooldown      = 0f;
        SkeletonSummonCooldown   = 0f;
        SealBreakerCooldown      = 0f;
        DarkRainCooldown         = 0f;
        ReaperCadenceCooldown    = 0f;
        ArcaneTorrentCooldown    = 0f;
        TwinCastCooldown         = 0f;
        ElementalRealignCooldown = 0f;
        ChainBreakCooldown       = 0f;
        ReaperFlurryCooldown     = 0f;
        ChainCaptureCooldown     = 0f;
        AbyssalRendCooldown      = 0f;
        FallingStarsCooldown     = 0f;
        ScytheMagicCooldown      = 0f;
        SealShardsCooldown       = 0f;
        SealArrayCooldown        = 0f;
        SkyDebrisCooldown        = 0f;
        SoulCopyCooldown         = 0f;
        InvertedSealCooldown     = 0f;
        ElementalRealignUses     = 0;
        ErodedCells              = 0;
        FinalMagicDone           = false;
        DistanceToPlayer         = 0f;
        LastChainCaptureAt       = NeverAt;
        LastBindBoltAt           = NeverAt;
        BindBoltHinted           = false;
        FloorUnstableTimer       = 0f;
        _seenCollapsed           = -1;
        _seenBroken              = -1;
    }

    /// <summary>
    /// 바닥 붕괴 · 일시 파괴가 새로 시작됐는지 칸 수로 잰다 — 아레나는 「흔들리는 중」을 내놓지 않는다.
    /// 시작된 순간부터 가장 긴 예고 + 낙하만큼 <see cref="FloorUnstable"/>.
    /// </summary>
    private void TrackFloor()
    {
        var grid = ArenaTileGrid.Active;
        if (grid == null) return;
        int collapsed = grid.CollapsedCount;
        int broken    = grid.BrokenCount;
        if (_seenCollapsed >= 0 && collapsed > _seenCollapsed) FloorUnstableTimer = Mathf.Max(FloorUnstableTimer, CollapseUnstableSeconds);
        if (_seenBroken    >= 0 && broken    > _seenBroken)    FloorUnstableTimer = Mathf.Max(FloorUnstableTimer, BreakUnstableSeconds);
        _seenCollapsed = collapsed;
        _seenBroken    = broken;
    }
}
}
