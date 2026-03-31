using Abyss.Monster;
using UnityEngine;

/// <summary>
/// DashSlash 패턴 SO. 고속 돌진 베기 데이터 + BKDashSlashState 소유.
///
/// 주 용도:
///   • Backstep 후 거리가 벌어진 경우 빠르게 플레이어에게 접근하며 타격
///   • Phase 2 (각성 후) 원거리에서 사용하는 급습 패턴
///
/// ChargeAttack 과 차이:
///   • 더 빠른 속도, 짧은 WindUp
///   • 피해는 도착 시 원형 AoE (돌진 중 히트박스 없음)
///   • AttackSpeedMult(각성 배율) 가 돌진 속도에 직접 반영
/// </summary>
[CreateAssetMenu(fileName = "BK_Pattern_DashSlash",
                 menuName  = "Abyss/Boss/BlackKnight/Patterns/DashSlash")]
public class BKDashSlashPatternSO : BossPatternSO
{
    // ── 사운드 ────────────────────────────────────────────
    [Header("사운드")]
    public AudioClip dashSfx;
    public AudioClip slashSfx;

    // ── DashSlash 데이터 ──────────────────────────────────
    [Header("DashSlash")]
    public string dashAnimState   = "Attack02";
    [Tooltip("쿨다운 (초)")]
    public float  dashCooldown    = 10f;
    [Tooltip("발동 최소 거리 (m)")]
    public float  dashMinDist     = 3f;
    [Tooltip("발동 최대 거리 (m)")]
    public float  dashMaxDist     = 12f;
    [Tooltip("돌진 속도 (m/s). AttackSpeedMult 에 의해 스케일됨.")]
    public float  dashSpeed       = 22f;
    [Tooltip("돌진 시작 전 바람업 시간 (초) — 경고 인디케이터 표시 구간")]
    public float  dashWindUp      = 0.18f;
    [Tooltip("돌진 이동 시간 (초)")]
    public float  dashDuration    = 0.35f;
    [Tooltip("도착 후 AoE 베기 범위 반경 (m)")]
    public float  slashRadius     = 2.5f;
    [Tooltip("베기 피해 배율")]
    public float  slashDamageMul  = 1.8f;
    [Tooltip("베기 동작 지속 시간 (초)")]
    public float  slashDuration   = 0.3f;
    public float  knockbackY      = 0.4f;
    public float  knockbackMul    = 2.2f;

    // ── 런타임 ────────────────────────────────────────────
    [System.NonSerialized] private BKDashSlashState _state;

    public override void Initialize(BossPatternContext ctx)
        => _state = new BKDashSlashState(this, ctx.Blackboard);

    public override bool CanExecute(BossPatternContext ctx)
        => _state != null && _state.CanExecute(ctx.Ctx);

    public override SpecialStateBase GetRuntimeState() => _state;
}
