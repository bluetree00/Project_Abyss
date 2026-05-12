namespace Abyss.Monster
{
/// <summary>
/// 보스 물리 상태 — 지상/공중.
/// 공중 공격 패턴 계열과 DragonBodyStateCondition 의 단일 진실 값.
/// Takeoff/Landing 전이 패턴이 이 값을 플립한다.
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

    /// <summary>
    /// 현재 바디 상태 — 지상/공중 판정의 단일 진실 값.
    /// DragonBodyStateCondition · 공중 공격 패턴들이 이 값을 기준으로 분기한다.
    /// </summary>
    public BodyState BodyState;

    /// <summary>
    /// Legacy 호환 proxy — 기존 코드의 `bb.IsAirborne = true/false` 설정을
    /// 그대로 유지하면서 내부적으로는 BodyState 를 갱신한다.
    /// 신규 코드는 BodyState 를 직접 사용할 것.
    /// </summary>
    public bool IsAirborne
    {
        get => BodyState == BodyState.Airborne;
        set => BodyState = value ? BodyState.Airborne : BodyState.Grounded;
    }

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
        BodyState       = BodyState.Grounded;
        AirBiteCooldown = 0f;
        IceSlamCooldown = 0f;
    }
}
}
