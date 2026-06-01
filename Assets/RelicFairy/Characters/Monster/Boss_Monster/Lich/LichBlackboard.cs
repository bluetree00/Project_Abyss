namespace RelicFairy.Monster
{
/// <summary>
/// 리치 보스 전용 블랙보드.
/// BossAttackBlackboard를 상속받아 리치 전용 상태(페이즈, 패턴 쿨다운)를 추가한다.
/// 패턴에서는 ctx.Blackboard를 LichBlackboard로 캐스팅하여 전용 필드에 접근한다.
/// </summary>
public class LichBlackboard : BossAttackBlackboard
{
    public bool IsPhase2 { get; private set; }

    // ── 패턴 쿨다운 ───────────────────────────────────────
    public float MagicBoltCooldown;
    public float TeleportStrikeCooldown;
    public float ElementalBarrageCooldown;
    public float ArcaneOrbCooldown;
    public float ScytheSweepCooldown;
    public float ScytheThrowCooldown;
    public float BlinkStrikeCooldown;
    public float SkeletonSummonCooldown;
    public float DeathRayCooldown;
    public float SealBreakerCooldown;
    public float DarkRainCooldown;

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
        if (DeathRayCooldown         > 0f) DeathRayCooldown         -= dt;
        if (SealBreakerCooldown      > 0f) SealBreakerCooldown      -= dt;
        if (DarkRainCooldown         > 0f) DarkRainCooldown         -= dt;
    }

    public void SetPhase2() => IsPhase2 = true;

    public new void Reset()
    {
        base.Reset();
        IsPhase2                 = false;
        MagicBoltCooldown        = 0f;
        TeleportStrikeCooldown   = 0f;
        ElementalBarrageCooldown = 0f;
        ArcaneOrbCooldown        = 0f;
        ScytheSweepCooldown      = 0f;
        ScytheThrowCooldown      = 0f;
        BlinkStrikeCooldown      = 0f;
        SkeletonSummonCooldown   = 0f;
        DeathRayCooldown         = 0f;
        SealBreakerCooldown      = 0f;
        DarkRainCooldown         = 0f;
        DistanceToPlayer         = 0f;
    }
}
}
