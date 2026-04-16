using UnityEngine;

namespace Abyss.Monster
{
/// <summary>
/// ForestGuardian 보스 전용 블랙보드.
/// 2페이즈 전환 상태, 그로기(강인도) 게이지를 담당한다.
///
/// ─ 페이즈 ─────────────────────────────────────────────────────
///  • Phase1 (HP 100%~50%)  : 기본 이동속도, 딜레이 0.8~1.8초, 애니 속도 1.0
///  • Phase2 (HP 50%~0%)    : 이동속도 1.25x, 딜레이 0.2~0.7초, 애니 속도 1.2
///  • HP ≤ 50% 진입 시 PhaseChangePending = true → FGPhaseTransitionPatternSO 발동
///
/// ─ 그로기(강인도) ─────────────────────────────────────────────
///  • 내려찍기·돌진 직후 플레이어 강공격 적중 시 ReduceGroggy() 호출
///  • GroggyGauge ≤ 0 → 3초 그로기(경직) 상태
///  • 그로기 중 IsGroggy == true → 패턴 SO가 판정 없음 처리
/// </summary>
public class ForestGuardianBlackboard
{
    public enum BossPhase { Phase1, Phase2 }

    // ── 페이즈 ──────────────────────────────────────────────────
    public BossPhase CurrentPhase      = BossPhase.Phase1;
    public bool      PhaseChangePending = false;

    public bool IsPhase2 => CurrentPhase == BossPhase.Phase2;

    // ── 그로기(강인도) ──────────────────────────────────────────
    public float GroggyGauge      = MaxGroggyGauge;
    public bool  IsGroggy          = false;
    public float GroggyTimer       = 0f;

    public const float MaxGroggyGauge = 100f;
    public const float GroggyDuration  = 3f;

    // ── Phase2 이동속도 / 애니 배율 (MonsterBase _ctx.Agent.speed에 적용) ──
    public const float Phase2SpeedMult        = 1.25f;
    public const float Phase2AnimSpeed        = 1.2f;

    // ── Phase2 패턴 브레이크 딜레이 (기획서: 0.2~0.7초) ─────────────────
    public const float Phase2BreakDurationMin = 0.2f;
    public const float Phase2BreakDurationMax = 0.7f;

    // ── 히트박스 보정: 돌진·회전킥 중 이동속도 비례 데미지 배율 ──────────
    /// <summary>돌진/회전킥 패턴에서 현재 이동 속도를 기준으로 데미지 배율을 계산한다.</summary>
    public float GetSpeedDamageMultiplier(float currentSpeed, float baseSpeed)
    {
        if (baseSpeed <= 0f) return 1f;
        return Mathf.Clamp(currentSpeed / baseSpeed, 1f, 2f);
    }

    // ── API ────────────────────────────────────────────────────

    /// <summary>HP ≤ 50% 감지 시 MonsterUpdate에서 호출.</summary>
    public void TryTriggerPhase2()
    {
        if (CurrentPhase == BossPhase.Phase1 && !PhaseChangePending)
            PhaseChangePending = true;
    }

    /// <summary>FGPhaseTransitionPatternSO 완료 시 호출.</summary>
    public void OnPhaseTransitionComplete()
    {
        CurrentPhase        = BossPhase.Phase2;
        PhaseChangePending  = false;
    }

    /// <summary>
    /// 그로기 게이지 감소. 0 이하이면 그로기 발동.
    /// 큰 공격 직후 플레이어 강공격 적중 시 MonsterBase 히트 처리에서 호출.
    /// </summary>
    public void ReduceGroggy(float amount)
    {
        if (IsGroggy) return;
        GroggyGauge = Mathf.Max(0f, GroggyGauge - amount);
        if (GroggyGauge <= 0f)
        {
            IsGroggy   = true;
            GroggyTimer = 0f;
            GroggyGauge = MaxGroggyGauge;
        }
    }

    /// <summary>Update() 매 프레임 호출 — 그로기 지속시간 감산.</summary>
    public void TickGroggy(float dt)
    {
        if (!IsGroggy) return;
        GroggyTimer += dt;
        if (GroggyTimer >= GroggyDuration)
            IsGroggy = false;
    }

    /// <summary>보스 풀 재사용(OnEnable) 시 초기화.</summary>
    public void Reset()
    {
        CurrentPhase        = BossPhase.Phase1;
        PhaseChangePending  = false;
        GroggyGauge         = MaxGroggyGauge;
        IsGroggy            = false;
        GroggyTimer         = 0f;
    }
}
}
