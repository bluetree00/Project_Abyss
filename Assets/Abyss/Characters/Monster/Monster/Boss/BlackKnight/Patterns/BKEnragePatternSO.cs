using Abyss.Monster;
using UnityEngine;

/// <summary>
/// Enrage(각성) 패턴 SO.
///
/// HP 40% 이하 최초 진입 시 1회만 발동 (CanExecute → !Blackboard.HasEnraged).
/// 발동 후에는 CanExecute = false → 해당 엔트리가 null 반환 → 다음 엔트리로 자동 넘어감.
///
/// breakOverride = 0f → 각성 직후 즉시 다음 패턴 발동 (전투 흐름 유지).
/// patternTag = "Enrage" → 필요 시 후속 엔트리에서 태그로 참조 가능.
/// </summary>
[CreateAssetMenu(fileName = "BK_Pattern_Enrage",
                 menuName  = "Abyss/Boss/BlackKnight/Patterns/Enrage")]
public class BKEnragePatternSO : BossPatternSO
{
    // ── 사운드 ────────────────────────────────────────────
    [Header("사운드")]
    public AudioClip enrageSfx;

    // ── Enrage 데이터 ─────────────────────────────────────
    [Header("Enrage")]
    public string enrageAnimState    = "Taunting";
    [Tooltip("각성 연출 정지 시간 (초) — 적을 바라보며 잠시 멈춤")]
    public float  enrageWindUp       = 0.6f;
    [Tooltip("포효 자세 유지 시간 (초)")]
    public float  enrageRoarDuration = 1.2f;
    [Tooltip("각성 후 AttackSpeedMult 값. Backstep/DashSlash 속도에 반영.")]
    public float  enrageSpeedMult    = 1.5f;
    [Tooltip("각성 후 공격력 배율 (곱셈). 1.25 = 25% 증가.")]
    public float  enrageDamageMult   = 1.25f;
    [Tooltip("각성 후 렌더러에 적용할 색조")]
    public Color  enrageTint         = new Color(1f, 0.3f, 0.2f);

    // ── 런타임 ────────────────────────────────────────────
    [System.NonSerialized] private BKEnrageState _state;

    public override void Initialize(BossPatternContext ctx)
        => _state = new BKEnrageState(this, ctx.Blackboard);

    /// <summary>아직 각성하지 않은 경우에만 실행 가능.</summary>
    public override bool CanExecute(BossPatternContext ctx)
        => _state != null && _state.CanExecute(ctx.Ctx);

    public override SpecialStateBase GetRuntimeState() => _state;
}
