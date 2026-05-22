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
    public float DeathRayCooldown;

    public new void TickCooldowns(float dt)
    {
        base.TickCooldowns(dt);
        if (MagicBoltCooldown      > 0f) MagicBoltCooldown      -= dt;
        if (TeleportStrikeCooldown > 0f) TeleportStrikeCooldown -= dt;
        if (DeathRayCooldown       > 0f) DeathRayCooldown       -= dt;
    }

    public void SetPhase2() => IsPhase2 = true;

    public new void Reset()
    {
        base.Reset();
        IsPhase2               = false;
        MagicBoltCooldown      = 0f;
        TeleportStrikeCooldown = 0f;
        DeathRayCooldown       = 0f;
    }
}
}
