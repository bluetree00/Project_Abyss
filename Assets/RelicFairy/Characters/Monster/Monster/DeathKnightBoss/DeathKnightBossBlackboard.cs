using UnityEngine;

namespace RelicFairy.Monster
{
/// <summary>DeathKnight 검 색상 (그리드 패턴 기준).</summary>
public enum DKSwordColor { White, Black }

/// <summary>
/// DeathKnight 전용 블랙보드.
///
/// ━━ 기능 ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
///  • 2페이즈 (HP ≤ condPhase2HpThreshold): 이동속도 1.2x, 패턴 딜레이 단축
///  • 격노(Enrage, HP ≤ enrageHpThreshold): 공격속도 1.4x, 이동속도 1.3x
///  격노는 1회만 발동하며 사망 전까지 해제되지 않는다.
/// ━━ 검 색상 ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
///  ChangeSlash(Attack1) 패턴 종료 시 FlipSwordColor()로 색상 반전.
/// </summary>
public class DeathKnightBossBlackboard
{
    // ── 페이즈/격노 상수 ──────────────────────────────────
    public const float EnrageHpThreshold   = 0.3f;
    public const float Phase2SpeedMult     = 1.2f;
    public const float EnrageSpeedMult     = 1.3f;
    public const float EnrageAnimSpeedMult = 1.4f;

    // ── 아머(Armor) 상수 ──────────────────────────────────
    public const float MaxArmor          = 60f;
    public const float NormalArmorDamage = 20f;  // 일반 피격: 3회 적중 시 파괴
    public const float HeavyArmorDamage  = 60f;  // 강공격: 1회에 즉시 파괴
    public const float ArmorStaggerTime  = 0.5f; // 아머 파괴 시 경직 지속 시간

    // ── 페이즈 ────────────────────────────────────────────
    public bool IsPhase2   { get; private set; }
    public bool IsEnraged  { get; private set; }

    // ── 아머 ─────────────────────────────────────────────
    public float Armor          { get; private set; } = MaxArmor;
    public bool  IsArmorBroken  { get; private set; }

    // ── 애니메이션 속도 배율 ──────────────────────────────
    public float AnimSpeedMult { get; private set; } = 1f;

    // ── 검 색상 ───────────────────────────────────────────
    public DKSwordColor SwordColor { get; private set; } = DKSwordColor.White;

    // ── 공개 API ──────────────────────────────────────────

    public void Reset()
    {
        IsPhase2      = false;
        IsEnraged     = false;
        AnimSpeedMult = 1f;
        Armor         = MaxArmor;
        IsArmorBroken = false;
        SwordColor    = DKSwordColor.White;
    }

    /// <summary>검 색상을 White↔Black 반전한다.</summary>
    public void FlipSwordColor()
    {
        SwordColor = SwordColor == DKSwordColor.White ? DKSwordColor.Black : DKSwordColor.White;
    }

    /// <summary>
    /// 아머 데미지 적용. 아머가 0 이하면 파괴 후 즉시 회복 → 경직 중 추가 파괴 방지.
    /// </summary>
    /// <returns>아머가 파괴됐으면 true.</returns>
    public bool ApplyArmorDamage(bool isHeavy)
    {
        if (IsArmorBroken) return false;

        Armor -= isHeavy ? HeavyArmorDamage : NormalArmorDamage;
        if (Armor <= 0f)
        {
            Armor         = MaxArmor;   // 파괴 즉시 회복
            IsArmorBroken = true;
            return true;
        }
        return false;
    }

    /// <summary>GetHitState 경직 종료 시 호출해 아머 파괴 플래그를 해제한다.</summary>
    public void ClearArmorBroken() => IsArmorBroken = false;

    public void SetPhase2()
    {
        if (IsPhase2) return;
        IsPhase2 = true;
    }

    /// <summary>
    /// 격노 상태 진입. 이미 격노 중이면 무시.
    /// AnimSpeedMult를 EnrageAnimSpeedMult로 갱신.
    /// </summary>
    public bool TrySetEnraged()
    {
        if (IsEnraged) return false;
        IsEnraged     = true;
        AnimSpeedMult = EnrageAnimSpeedMult;
        return true;
    }
}
}
