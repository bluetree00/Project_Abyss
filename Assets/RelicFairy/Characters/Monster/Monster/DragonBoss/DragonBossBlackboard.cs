using UnityEngine;

namespace RelicFairy.Monster
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
    /// <summary>
    /// 드래곤 브레스 원소 종류. BossColumnHazard 에서 사용.
    /// Abyss = 2페이지 「심연의 화룡」 동안 고정되는 네 번째 원소(검은 불). 직렬화 값이라 맨 뒤에 붙인다.
    /// </summary>
    public enum DragonElement { Fire, Ice, Thunder, Abyss }

    public bool HasSummonedAt70;
    public bool HasSummonedAt40;
    public bool HasSummonedAt10;

    /// <summary>
    /// 현재 바디 상태 — 지상/공중 판정의 단일 진실 값.
    /// DragonBodyStateCondition · 공중 공격 패턴들이 이 값을 기준으로 분기한다.
    /// </summary>
    public BodyState BodyState;
    public int GroundedPatternStreak;
    public int AirbornePatternStreak;
    public float TakeoffBaseWeight = 1f;
    public float LandingBaseWeight = 1f;
    public float AirOrbitAccumulatedDegrees;

    /// <summary>
    /// Summon 패턴의 공중 대기 루프에서 BreathSweep/FireballRain 패턴으로 핸드오프했을 때,
    /// 해당 패턴 종료 후 복귀할 상태. null이면 평소처럼 AttackReadyState로 복귀한다.
    /// </summary>
    public SpecialStateBase AirLoopReturnState;

    /// <summary>소환 패턴 임계값 돌파 후 소환 완료까지 데미지를 차단하는 무적 게이트.</summary>
    public bool IsSummonGated { get; private set; }
    public void SetSummonGated(bool value) => IsSummonGated = value;

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
    /// <summary>할퀴기 전용 쿨다운 — 예전엔 공중 패턴과 LeapCooldown을 같이 써서 착지 뒤 할퀴기가 자주 잠겼다(09-28).</summary>
    public float ClawSlashCooldown;
    /// <summary>지상 브레스 쿨다운 — 없을 땐 「착지 → 브레스 → 이륙」이 고정 순환이 됐다(09-28).</summary>
    public float GroundBreathCooldown;

    // ── 2페이지 「심연의 화룡」 (09-28 설계 §4) ─────────────
    public float BlackFlameStormCooldown;   // DL1 흑염 폭풍
    public float ElementConcertCooldown;    // DL2 원소 합주
    public float VoidFallCooldown;          // DL3 공허 낙하
    public float AbyssDiveCooldown;         // DL4 심연 급강하
    /// <summary>공허 낙하 · 검은 태양 동안 수동 운석을 멈춘다 — 기둥 뒤 · 날개 아래에 숨은 자리에 운석이 떨어지지 않게.</summary>
    public bool  PassiveMeteorSuppressed;

    // ── 피격 방향 ─────────────────────────────────────────
    public enum HitDirection { Front, Back, Left, Right }
    public HitDirection LastHitDirection { get; private set; }

    // ── 쉴드(Poise) ──────────────────────────────────────
    public const float MaxPoise          = 60f;
    public const float NormalPoiseDamage = 20f;
    public const float PoiseStaggerTime  = 0.5f;

    public float Poise         { get; private set; } = MaxPoise;
    public bool  IsPoiseBroken { get; private set; }

    public void SetHitDirection(Vector3 instigatorDir, Vector3 monsterForward)
    {
        Vector3 dir = instigatorDir;
        dir.y = 0f;
        if (dir.sqrMagnitude < 0.001f) { LastHitDirection = HitDirection.Front; return; }
        dir.Normalize();

        float fwd   = Vector3.Dot(dir, monsterForward);
        float right = Vector3.Dot(dir, Vector3.Cross(Vector3.up, monsterForward).normalized * -1f);

        if (Mathf.Abs(fwd) >= Mathf.Abs(right))
            LastHitDirection = fwd >= 0f ? HitDirection.Front : HitDirection.Back;
        else
            LastHitDirection = right >= 0f ? HitDirection.Right : HitDirection.Left;
    }

    /// <returns>쉴드가 파괴됐으면 true.</returns>
    public bool ApplyPoiseDamage()
    {
        if (IsPoiseBroken) return false;
        Poise -= NormalPoiseDamage;
        if (Poise <= 0f)
        {
            Poise         = MaxPoise;
            IsPoiseBroken = true;
            return true;
        }
        return false;
    }

    public void ClearPoiseBroken() => IsPoiseBroken = false;

    public new void TickCooldowns(float deltaTime)
    {
        base.TickCooldowns(deltaTime);
        if (AirBiteCooldown > 0f) AirBiteCooldown -= deltaTime;
        if (IceSlamCooldown > 0f) IceSlamCooldown -= deltaTime;
        if (ClawSlashCooldown > 0f) ClawSlashCooldown -= deltaTime;
        if (GroundBreathCooldown > 0f) GroundBreathCooldown -= deltaTime;
        if (BlackFlameStormCooldown > 0f) BlackFlameStormCooldown -= deltaTime;
        if (ElementConcertCooldown  > 0f) ElementConcertCooldown  -= deltaTime;
        if (VoidFallCooldown        > 0f) VoidFallCooldown        -= deltaTime;
        if (AbyssDiveCooldown       > 0f) AbyssDiveCooldown       -= deltaTime;
    }

    public new void Reset()
    {
        base.Reset();
        HasSummonedAt70 = false;
        HasSummonedAt40 = false;
        HasSummonedAt10 = false;
        BodyState             = BodyState.Grounded;
        GroundedPatternStreak = 0;
        AirbornePatternStreak = 0;
        TakeoffBaseWeight     = 1f;
        LandingBaseWeight     = 1f;
        AirOrbitAccumulatedDegrees = 0f;
        AirLoopReturnState    = null;
        AirBiteCooldown       = 0f;
        IceSlamCooldown       = 0f;
        ClawSlashCooldown     = 0f;
        GroundBreathCooldown  = 0f;
        BlackFlameStormCooldown = 0f;
        ElementConcertCooldown  = 0f;
        VoidFallCooldown        = 0f;
        AbyssDiveCooldown       = 0f;
        PassiveMeteorSuppressed = false;
        Poise                = MaxPoise;
        IsPoiseBroken         = false;
        LastHitDirection      = HitDirection.Front;
        IsSummonGated         = false;
    }
}
}
