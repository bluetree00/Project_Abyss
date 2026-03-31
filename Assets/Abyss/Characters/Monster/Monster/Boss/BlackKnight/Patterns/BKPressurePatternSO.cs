using Abyss.Monster;
using UnityEngine;

/// <summary>
/// Pressure 패턴 SO. 압박 연타 콤보 데이터 + BKPressureState 소유.
///
/// Phase 1: 2타 연속 (Hit1 → Step → Hit2)
/// Phase 2 (각성 후, HasEnraged): 3타 연속 (Hit1 → Step → Hit2 → StepFwd → Hit3)
///
/// 소울류 전투 설계 원칙:
///   • 짧은 WindUp으로 반응 압박 → 플레이어가 적극적으로 회피해야 함
///   • 각 타격 사이 짧은 전진 스텝으로 플레이어의 거리 확보 차단
///   • 각성 후 3타에서 마지막 타가 넓은 범위 → 백스텝만으로는 벗어나기 어려움
/// </summary>
[CreateAssetMenu(fileName = "BK_Pattern_Pressure",
                 menuName  = "Abyss/Boss/BlackKnight/Patterns/Pressure")]
public class BKPressurePatternSO : BossPatternSO
{
    // ── 사운드 ────────────────────────────────────────────
    [Header("사운드")]
    public AudioClip pressureWindUpSfx;
    public AudioClip hit1Sfx;
    public AudioClip hit2Sfx;
    public AudioClip hit3Sfx;

    // ── 공통 ──────────────────────────────────────────────
    [Header("공통")]
    [Tooltip("발동 최대 거리 (m). 이 범위 내에 있어야 실행.")]
    public float pressureRange   = 4f;
    [Tooltip("콤보 쿨다운 (초)")]
    public float pressureCooldown = 9f;
    public float knockbackY      = 0.3f;
    public float knockbackMul    = 1.2f;
    [Tooltip("타격 사이 전진 거리 (m)")]
    public float stepDistance    = 0.8f;

    // ── WindUp ────────────────────────────────────────────
    [Header("WindUp")]
    public float  windUpDuration = 0.2f;

    // ── Hit 1 (빠른 횡 베기) ─────────────────────────────
    [Header("Hit1 — 빠른 횡 베기")]
    public string hit1AnimState  = "Attack01";
    public float  hit1Duration   = 0.38f;
    [Tooltip("타격 판정이 발생하는 시간 (초)")]
    public float  hitTime1       = 0.22f;
    public float  hit1Radius     = 2.2f;
    public float  hit1DamageMul  = 0.8f;

    // ── Hit 2 (묵직한 내려 베기) ─────────────────────────
    [Header("Hit2 — 묵직한 내려 베기")]
    public string hit2AnimState  = "Attack05";
    public float  hit2Duration   = 0.5f;
    [Tooltip("타격 판정이 발생하는 시간 (초)")]
    public float  hitTime2       = 0.28f;
    public float  hit2Radius     = 2.5f;
    public float  hit2DamageMul  = 1.1f;

    // ── Hit 3 (각성 전용 — 회전 강타) ────────────────────
    [Header("Hit3 — 각성 전용 회전 강타")]
    public string hit3AnimState  = "Attack06";
    public float  hit3Duration   = 0.6f;
    [Tooltip("타격 판정이 발생하는 시간 (초)")]
    public float  hitTime3       = 0.32f;
    public float  hit3Radius     = 3.2f;
    public float  hit3DamageMul  = 1.4f;

    // ── 런타임 ────────────────────────────────────────────
    [System.NonSerialized] private BKPressureState _state;

    public override void Initialize(BossPatternContext ctx)
        => _state = new BKPressureState(this, ctx.Blackboard);

    public override bool CanExecute(BossPatternContext ctx)
        => _state != null && _state.CanExecute(ctx.Ctx);

    public override SpecialStateBase GetRuntimeState() => _state;
}
