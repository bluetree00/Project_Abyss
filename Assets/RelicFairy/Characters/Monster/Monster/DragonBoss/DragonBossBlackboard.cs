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
    /// <summary>드래곤 브레스 원소 종류. BossColumnHazard 에서 사용.</summary>
    public enum DragonElement { Fire, Ice, Thunder }

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
        Poise                 = MaxPoise;
        IsPoiseBroken         = false;
        LastHitDirection      = HitDirection.Front;
        IsSummonGated         = false;
    }
}
}
