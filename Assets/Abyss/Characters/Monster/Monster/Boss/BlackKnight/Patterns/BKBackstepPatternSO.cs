using Abyss.Monster;
using UnityEngine;

/// <summary>
/// Backstep 패턴 SO. 후퇴 회피 데이터 + BKBackstepState 소유.
///
/// breakOverride = 0.4 로 설정 → Backstep 완료 직후 0.4 초 만에 후속 패턴 발동.
/// patternTag = "Backstep" → BK_Cond_AfterBackstep 조건과 연계.
/// </summary>
[CreateAssetMenu(fileName = "BK_Pattern_Backstep",
                 menuName  = "Abyss/Boss/BlackKnight/Patterns/Backstep")]
public class BKBackstepPatternSO : BossPatternSO
{
    // ── 사운드 ────────────────────────────────────────────
    [Header("사운드")]
    public AudioClip backstepSfx;

    // ── Backstep 데이터 ───────────────────────────────────
    [Header("Backstep")]
    public string backstepAnimState  = "Dodge";
    [Tooltip("후퇴 발동 쿨다운 (초)")]
    public float  backstepCooldown   = 6f;
    [Tooltip("후퇴 이동 거리 (m)")]
    public float  backstepDistance   = 4.5f;
    [Tooltip("후퇴 이동 시간 (초). AttackSpeedMult 로 스케일됨.")]
    public float  backstepDuration   = 0.4f;
    [Tooltip("이동 시작 전 준비 시간 (초)")]
    public float  backstepWindUp     = 0.12f;

    // ── 런타임 ────────────────────────────────────────────
    [System.NonSerialized] private BKBackstepState _state;

    public override void Initialize(BossPatternContext ctx)
        => _state = new BKBackstepState(this, ctx.Blackboard);

    public override bool CanExecute(BossPatternContext ctx)
        => _state != null && _state.CanExecute(ctx.Ctx);

    public override SpecialStateBase GetRuntimeState() => _state;
}
