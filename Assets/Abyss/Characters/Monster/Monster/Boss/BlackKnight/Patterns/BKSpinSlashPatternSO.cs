using Abyss.Monster;
using UnityEngine;

/// <summary>
/// SpinSlash 패턴 SO.
/// HP 임계값 목록을 관리하며, BKSpinSlashState 인스턴스를 소유한다.
///
/// BossConfigSO Inspector 에서:
///  • 강제 실행 엔트리: forceExecute=true, conditions=[BKSpinPhaseConditionSO], patterns=[이 SO]
///  • 일반 가중치 풀 엔트리: conditions=[], patterns=[...이 SO 포함...]
/// </summary>
[CreateAssetMenu(fileName = "BK_Pattern_SpinSlash",
                 menuName  = "Abyss/Boss/BlackKnight/Patterns/SpinSlash")]
public class BKSpinSlashPatternSO : BossPatternSO
{
    // ── 공통 ─────────────────────────────────────────────
    [Header("공통")]
    public float postAttackDelay = 0.4f;

    // ── 사운드 ────────────────────────────────────────────
    [Header("사운드")]
    public AudioClip spinSfx;

    // ── SpinSlash 데이터 ──────────────────────────────────
    [Header("SpinSlash")]
    public string spinAnimState     = "SpinAttack";
    [Tooltip("전체 지속 시간 (초)")]
    public float  spinDuration      = 10f;
    [Tooltip("차지 구간 비율 (0~1)")]
    public float  spinChargeRatio   = 0.2f;
    [Tooltip("마무리 구간 비율 (0~1)")]
    public float  spinWindDownRatio = 0.2f;
    public float  spinCooldown      = 6f;
    [Tooltip("발동 HP 임계값 목록 (내림차순). 각 구간에서 한 번씩 발동.")]
    public float[] spinHpThresholds = { 0.85f, 0.55f, 0.25f };
    public float  spinRadius        = 3.5f;
    public float  spinDamageMul     = 0.4f;
    public float  spinHitInterval   = 0.55f;
    public float  spinFlashDuration = 0.15f;
    public float  spinChargeFreqMin = 4f;
    public float  spinChargeFreqMax = 18f;
    public float  spinSpinFreqMin   = 6f;
    public float  spinSpinFreqMax   = 24f;
    public float  spinRadiusMul1    = 1.5f;
    public float  spinRadiusMul2    = 1.9f;
    public float  spinRadiusMul3    = 2.4f;
    public float  spinKnockbackY    = 0.4f;
    public float  spinKnockbackMul  = 1.5f;

    // ── 런타임 (비직렬화) ────────────────────────────────
    [System.NonSerialized] private BKSpinSlashState _state;

    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    // BossPatternSO 오버라이드
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

    public override void Initialize(BossPatternContext ctx)
    {
        _state = new BKSpinSlashState(this, ctx.Blackboard);
    }

    public override void OnRecycled()
    {
        _state?.ResetThresholds();
    }

    public override bool CanExecute(BossPatternContext ctx)
        => _state != null && _state.CanExecute(ctx.Ctx);

    /// <summary>
    /// 강제 인터럽트 판정 — 거리 무관, HP 임계값만 체크.
    /// Force 엔트리에서 현재 패턴이 끝나는 즉시 SpinSlash 로 진입할지 결정한다.
    /// </summary>
    public override bool CanForceInterrupt(BossPatternContext ctx)
        => _state != null && _state.IsHpThresholdMet(ctx.Ctx);

    public override SpecialStateBase GetRuntimeState() => _state;

    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    // SpinSlash 전용 API
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

    /// <summary>거리 무관하게 HP 임계값 충족 여부만 확인.</summary>
    public bool IsHpThresholdMet(BossPatternContext ctx)
        => _state != null && _state.IsHpThresholdMet(ctx.Ctx);
}
