using Abyss.Monster;
using UnityEngine;

/// <summary>
/// OverheadSlash 패턴 SO. 기본 강타 데이터 + BKOverheadSlashState 소유.
/// minBasicAttackDuration 은 BKNormalModeTimerConditionSO 의 minDuration 과 동일하게 설정한다.
/// </summary>
[CreateAssetMenu(fileName = "BK_Pattern_OverheadSlash",
                 menuName  = "Abyss/Boss/BlackKnight/Patterns/OverheadSlash")]
public class BKOverheadSlashPatternSO : BossPatternSO
{
    // ── 사운드 ────────────────────────────────────────────
    [Header("사운드")]
    public AudioClip overheadSfx;

    // ── OverheadSlash 데이터 ──────────────────────────────
    [Header("OverheadSlash")]
    public string overheadAnimState  = "Attack02";
    [Tooltip("공격 애니메이션 지속 시간 (초)")]
    public float  overheadDuration   = 1.2f;
    [Tooltip("히트 판정 시작 시간 (Enter 후 몇 초 뒤)")]
    public float  overheadHitTime    = 0.5f;
    public float  overheadDamageMul  = 1.2f;
    [Tooltip("히트 범위 반경 (m)")]
    public float  overheadRadius     = 7.5f;
    public float  overheadCooldown   = 5f;
    public float  overheadKnockbackY = 0.3f;
    [Tooltip("패턴 직후 즉시 재발동을 막기 위해 평타 모드로 최소 유지해야 하는 시간 (초).\n" +
             "BKNormalModeTimerConditionSO 의 minDuration 과 값을 맞출 것.")]
    public float  minBasicAttackDuration = 3f;

    // ── 런타임 ────────────────────────────────────────────
    [System.NonSerialized] private BKOverheadSlashState _state;

    public override void Initialize(BossPatternContext ctx)
    {
        _state = new BKOverheadSlashState(this, ctx.Blackboard);
    }

    public override bool CanExecute(BossPatternContext ctx)
        => _state != null && _state.CanExecute(ctx.Ctx);

    public override SpecialStateBase GetRuntimeState() => _state;
}
