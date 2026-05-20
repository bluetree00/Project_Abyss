using UnityEngine;

namespace Abyss.Monster
{
/// <summary>
/// DeathKnight 전용 블랙보드.
///
/// ━━ 기능 ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
///  • 2페이즈 (HP ≤ condPhase2HpThreshold): 이동속도 1.2x, 패턴 딜레이 단축
///  • 격노(Enrage, HP ≤ enrageHpThreshold): 공격속도 1.4x, 이동속도 1.3x
///  격노는 1회만 발동하며 사망 전까지 해제되지 않는다.
/// </summary>
public class DeathKnightBossBlackboard
{
    // ── 상수 ─────────────────────────────────────────────
    public const float EnrageHpThreshold   = 0.3f;
    public const float Phase2SpeedMult     = 1.2f;
    public const float EnrageSpeedMult     = 1.3f;
    public const float EnrageAnimSpeedMult = 1.4f;

    // ── 페이즈 ────────────────────────────────────────────
    public bool IsPhase2   { get; private set; }
    public bool IsEnraged  { get; private set; }

    // ── 애니메이션 속도 배율 ──────────────────────────────
    public float AnimSpeedMult { get; private set; } = 1f;

    // ── 공개 API ──────────────────────────────────────────

    public void Reset()
    {
        IsPhase2      = false;
        IsEnraged     = false;
        AnimSpeedMult = 1f;
    }

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
