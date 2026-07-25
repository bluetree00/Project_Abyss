using UnityEngine;

namespace RelicFairy.Monster
{
/// <summary>
/// ForestGuardian 전용 블랙보드.
///
/// 강인도(Toughness), 그로기, 빅어택 윈도우, 페이즈 상태를 관리한다.
/// MonsterBase.Update → ForestGuardianMonster.Update 에서 Tick 호출.
/// </summary>
public class ForestGuardianBlackboard
{
    // ── 상수 ─────────────────────────────────────────────
    public const float MaxToughness       = 100f;
    public const float GroggyDuration     = 3f;
    public const float BigWindowDuration  = 2f;
    public const float NormalHitDamage    = 8f;
    public const float BigWindowHitDamage = 40f;

    // ── 방어도(Poise) 상수 ───────────────────────────────
    public const float MaxPoise          = 50f;
    public const float NormalPoiseDamage = 5f;   // 일반 피격: 10회 적중 시 파괴
    public const float HeavyPoiseDamage  = 50f;  // 빅윈도우 피격: 1회에 즉시 파괴
    public const float PoiseStaggerTime  = 0.7f; // 방어도 파괴 시 경직 지속 시간

    // ── 강인도 ────────────────────────────────────────────
    public float Toughness    { get; private set; } = MaxToughness;
    public bool  IsGroggy     { get; private set; }
    public float GroggyTimer  { get; private set; }

    // ── 방어도(Poise) ────────────────────────────────────
    public float Poise         { get; private set; } = MaxPoise;
    public bool  IsPoiseBroken { get; private set; }

    // ── 빅어택 윈도우 ─────────────────────────────────────
    public float BigAttackWindow { get; private set; }
    public bool  IsBigWindowOpen => BigAttackWindow > 0f;

    // ── 페이즈 ────────────────────────────────────────────
    public bool  IsPhase2        { get; private set; }
    public bool  IsTransitioning { get; private set; }
    public float AnimSpeedMult   { get; private set; } = 1f;

    // ── 피격 방향 ─────────────────────────────────────────
    public enum HitDirection { Front, Back, Left, Right, Heavy }
    public HitDirection LastHitDirection { get; private set; }

    /// <summary>
    /// 피격 방향을 저장한다.
    /// instigatorDir: 공격자 위치 - 피격자 위치 (월드 공간).
    /// isHeavy: 빅윈도우 피격 등 강한 공격 여부.
    /// </summary>
    public void SetHitDirection(Vector3 instigatorDir, Vector3 monsterForward, bool isHeavy)
    {
        if (isHeavy) { LastHitDirection = HitDirection.Heavy; return; }

        Vector3 dir = instigatorDir; dir.y = 0f;
        if (dir.sqrMagnitude < 0.001f) { LastHitDirection = HitDirection.Front; return; }
        dir.Normalize();

        float forward = Vector3.Dot(dir, monsterForward);
        float right   = Vector3.Dot(dir, Vector3.Cross(Vector3.up, monsterForward).normalized * -1f);

        // |forward| vs |right| 중 더 큰 축으로 판별
        if (Mathf.Abs(forward) >= Mathf.Abs(right))
            LastHitDirection = forward >= 0f ? HitDirection.Front : HitDirection.Back;
        else
            LastHitDirection = right >= 0f ? HitDirection.Right : HitDirection.Left;
    }

    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    // 공개 API
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

    /// <summary>풀 재사용 시 초기화.</summary>
    public void Reset()
    {
        Toughness       = MaxToughness;
        IsGroggy        = false;
        GroggyTimer     = 0f;
        BigAttackWindow = 0f;
        IsPhase2        = false;
        IsTransitioning = false;
        AnimSpeedMult   = 1f;
        Poise           = MaxPoise;
        IsPoiseBroken   = false;
    }

    /// <summary>
    /// 매 프레임 호출. 빅어택 윈도우 및 그로기 타이머를 감소시킨다.
    /// 그로기 타이머 종료 시 강인도를 MaxToughness 로 복원하고 그로기 해제.
    /// </summary>
    public void Tick(float dt)
    {
        if (BigAttackWindow > 0f)
            BigAttackWindow -= dt;

        if (IsGroggy)
        {
            GroggyTimer -= dt;
            if (GroggyTimer <= 0f)
            {
                IsGroggy    = false;
                GroggyTimer = 0f;
                Toughness   = MaxToughness;
            }
        }
    }

    /// <summary>
    /// 강인도 데미지 적용.
    /// isBigWindowHit=true 이면 BigWindowHitDamage 추가.
    /// 강인도 0 이하 시 그로기 진입.
    /// </summary>
    public void ApplyToughnessDamage(float amount, bool isBigWindowHit = false)
    {
        if (IsGroggy) return;

        float total = amount + (isBigWindowHit ? BigWindowHitDamage : 0f);
        Toughness -= total;

        if (Toughness <= 0f)
        {
            Toughness   = 0f;
            IsGroggy    = true;
            GroggyTimer = GroggyDuration;
            BigAttackWindow = 0f;
        }
    }

    /// <summary>
    /// 방어도 데미지 적용. 방어도가 0 이하면 파괴 상태로 전환 후 즉시 회복.
    /// isHeavy=true (빅윈도우 피격)이면 HeavyPoiseDamage 적용 → 1회에 파괴.
    /// 그로기 중이면 적용하지 않는다.
    /// </summary>
    /// <returns>방어도가 파괴됐으면 true.</returns>
    public bool ApplyPoiseDamage(bool isHeavy)
    {
        if (IsGroggy) return false;
        if (IsPoiseBroken) return false;

        Poise -= isHeavy ? HeavyPoiseDamage : NormalPoiseDamage;
        if (Poise <= 0f)
        {
            Poise         = MaxPoise;   // 파괴 즉시 회복 → 경직 중 추가 파괴 방지
            IsPoiseBroken = true;
            return true;
        }
        return false;
    }

    /// <summary>GetHitState 경직 종료 시 호출해 방어도 파괴 플래그를 해제한다.</summary>
    public void ClearPoiseBroken() => IsPoiseBroken = false;

    /// <summary>빅어택 윈도우를 BigWindowDuration 만큼 열어준다.</summary>
    public void TriggerBigAttackWindow()
    {
        BigAttackWindow = BigWindowDuration;
    }

    /// <summary>2페이즈 전환 완료 시 호출.</summary>
    public void SetPhase2()
    {
        IsPhase2      = true;
        AnimSpeedMult = 1.2f;
    }

    /// <summary>2페이즈 변신 연출 진행 중 여부. 변신 중에는 받는 피해 99% 감소.</summary>
    public void SetTransitioning(bool value) => IsTransitioning = value;
}
}
