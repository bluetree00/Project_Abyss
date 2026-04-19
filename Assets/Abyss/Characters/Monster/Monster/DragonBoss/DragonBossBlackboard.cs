namespace Abyss.Monster
{
/// <summary>
/// 보스 물리 상태 — 지상/공중.
/// 장래 L4 전이 패턴(Takeoff/Landing) 만이 이 값을 플립하도록 설계.
/// 현재는 DragonBodyStateCondition 이 레거시 IsAirborne 플래그에서 파생 (P7 에서 전환).
/// </summary>
public enum BodyState
{
    Grounded,
    Airborne,
}

/// <summary>
/// DragonBoss 전용 블랙보드.
/// 공용 쿨다운/타이머 외에 드래곤 고유 상태 플래그를 추가한다.
/// </summary>
public class DragonBossBlackboard : BossAttackBlackboard
{
    /// <summary>드래곤 브레스 원소 종류. BossColumnHazard 에서 사용.</summary>
    public enum DragonElement { Fire, Ice, Thunder }

    public bool HasSummonedAt80;
    public bool HasSummonedAt50;
    public bool HasSummonedAt10;
    public bool IsAirborne;
    public float AirBiteCooldown;
    public float IceSlamCooldown;

    public new void TickCooldowns(float deltaTime)
    {
        base.TickCooldowns(deltaTime);
        if (AirBiteCooldown > 0f) AirBiteCooldown -= deltaTime;
        if (IceSlamCooldown > 0f) IceSlamCooldown -= deltaTime;
    }

    public new void Reset()
    {
        base.Reset();
        HasSummonedAt80 = false;
        HasSummonedAt50 = false;
        HasSummonedAt10 = false;
        IsAirborne      = false;
        AirBiteCooldown = 0f;
        IceSlamCooldown = 0f;
    }
}
}
