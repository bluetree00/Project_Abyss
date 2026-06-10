using System;
using UnityEngine;

/// <summary>
/// 가웨인 고유 메커닉: 태양 타이머.
///
/// 사이클:
///   강화 구간 (SolarPhase.Empowered): 20초 — 공격력 3배 + 방어력 +25%
///   약화 구간 (SolarPhase.Weakened) : 15초 — 공격력 ×0.8
///
/// 전략 포인트:
///   강화 구간 = 적극 공세, E스킬 사용
///   약화 구간 = 회피, 체력 회복
/// </summary>
public class SolarTimer : MonoBehaviour
{
    // ── 상수 ─────────────────────────────────────────────────────────────────
    public const float EmpoweredDuration = 20f;
    public const float WeakenedDuration  = 15f;

    private const float EmpoweredAttackMult  = 3.0f;   // 정오의 서약 +200% → ×3
    private const float WeakenedAttackMult   = 0.8f;   // 약화 -20%
    public const float EmpoweredDefenseMult = 1.30f;   // 강화 구간 방어력 +30%

    // ── 이벤트 ───────────────────────────────────────────────────────────────
    public event Action<SolarPhase> OnPhaseChanged;

    // ── 상태 ─────────────────────────────────────────────────────────────────
    public SolarPhase CurrentPhase { get; private set; } = SolarPhase.Empowered;
    public float PhaseTimer        { get; private set; }
    public float PhaseProgress     => CurrentPhase == SolarPhase.Empowered
        ? PhaseTimer / EmpoweredDuration
        : PhaseTimer / WeakenedDuration;

    public bool IsEmpowered => CurrentPhase == SolarPhase.Empowered;

    private PlayerRuntimeStats _stats;

    // ── 초기화 ───────────────────────────────────────────────────────────────

    public void Initialize(PlayerRuntimeStats stats)
    {
        _stats = stats;
        EnterPhase(SolarPhase.Empowered);
    }

    // ── Unity Lifecycle ───────────────────────────────────────────────────────

    private void Update()
    {
        PhaseTimer -= Time.deltaTime;
        if (PhaseTimer <= 0f)
            EnterPhase(CurrentPhase == SolarPhase.Empowered ? SolarPhase.Weakened : SolarPhase.Empowered);
    }

    // ── 내부 ─────────────────────────────────────────────────────────────────

    private void EnterPhase(SolarPhase phase)
    {
        CurrentPhase = phase;

        if (phase == SolarPhase.Empowered)
        {
            PhaseTimer = EmpoweredDuration;
            _stats?.SetCharacterAttackMultiplier(EmpoweredAttackMult, EmpoweredAttackMult);
        }
        else
        {
            PhaseTimer = WeakenedDuration;
            _stats?.SetCharacterAttackMultiplier(WeakenedAttackMult, WeakenedAttackMult);
        }

        OnPhaseChanged?.Invoke(phase);
    }
}

public enum SolarPhase
{
    Empowered,
    Weakened,
}
