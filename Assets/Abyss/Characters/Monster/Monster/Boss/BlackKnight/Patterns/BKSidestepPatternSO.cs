using Abyss.Monster;
using UnityEngine;

/// <summary>
/// Sidestep 패턴 SO. 측면 이동 플랭크 데이터 + BKSidestepState 소유.
///
/// 보스가 플레이어의 측면으로 순간 이동 후 즉시 공격 연계.
/// breakOverride = 0.2 → 이동 직후 0.2초 만에 후속 패턴 발동.
/// patternTag = "Sidestep" → BK_Cond_AfterSidestep 조건과 연계.
///
/// 쿨다운은 Backstep 과 공유 (BackstepCooldown 사용).
/// → 후퇴/측면 이동 두 기동 패턴이 너무 자주 연달아 나오지 않도록.
/// </summary>
[CreateAssetMenu(fileName = "BK_Pattern_Sidestep",
                 menuName  = "Abyss/Boss/BlackKnight/Patterns/Sidestep")]
public class BKSidestepPatternSO : BossPatternSO
{
    // ── 사운드 ────────────────────────────────────────────
    [Header("사운드")]
    public AudioClip sidestepSfx;

    // ── Sidestep 데이터 ───────────────────────────────────
    [Header("Sidestep")]
    public string sidestepAnimState  = "Dodge";
    [Tooltip("발동 최대 거리 (m)")]
    public float  sidestepRange      = 5f;
    [Tooltip("BackstepCooldown 에 설정될 값 (초)")]
    public float  sidestepCooldown   = 5f;
    [Tooltip("측면 이동 거리 (m)")]
    public float  sidestepDistance   = 2.5f;
    [Tooltip("측면 이동 시간 (초). HasEnraged 시 1.2배 가속.")]
    public float  sidestepDuration   = 0.22f;

    // ── 런타임 ────────────────────────────────────────────
    [System.NonSerialized] private BKSidestepState _state;

    public override void Initialize(BossPatternContext ctx)
        => _state = new BKSidestepState(this, ctx.Blackboard);

    public override bool CanExecute(BossPatternContext ctx)
        => _state != null && _state.CanExecute(ctx.Ctx);

    public override SpecialStateBase GetRuntimeState() => _state;
}
