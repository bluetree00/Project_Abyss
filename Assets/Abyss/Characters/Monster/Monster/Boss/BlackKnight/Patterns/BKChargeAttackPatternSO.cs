using Abyss.Monster;
using UnityEngine;

/// <summary>ChargeAttack 패턴 SO. 돌진 데이터 + BKChargeAttackState 소유.</summary>
[CreateAssetMenu(fileName = "BK_Pattern_ChargeAttack",
                 menuName  = "Abyss/Boss/BlackKnight/Patterns/ChargeAttack")]
public class BKChargeAttackPatternSO : BossPatternSO
{
    // ── 사운드 ────────────────────────────────────────────
    [Header("사운드")]
    public AudioClip chargeSfx;

    // ── ChargeAttack 데이터 ───────────────────────────────
    [Header("ChargeAttack")]
    public string chargeAnimState    = "Attack03";
    public float  chargeDuration     = 1.4f;
    public float  chargeCooldown     = 8f;
    [Tooltip("돌진 발동 최소 거리 (m)")]
    public float  chargeMinDist      = 4f;
    [Tooltip("돌진 발동 최대 거리 (m)")]
    public float  chargeMaxDist      = 9f;
    public float  chargeSpeed        = 14f;
    public float  chargeDamageMul    = 1.5f;
    [Tooltip("돌진 히트 범위 반경 (m)")]
    public float  chargeRadius       = 2f;
    [Tooltip("바람업 비율 (0~1). 이 비율 동안 경고 후 돌진 시작.")]
    public float  chargeWindUpRatio  = 0.3f;
    public float  chargeKnockbackY   = 0.5f;
    public float  chargeKnockbackMul = 2.5f;

    // ── 런타임 ────────────────────────────────────────────
    [System.NonSerialized] private BKChargeAttackState _state;

    public override void Initialize(BossPatternContext ctx)
    {
        _state = new BKChargeAttackState(this, ctx.Blackboard);
    }

    public override bool CanExecute(BossPatternContext ctx)
        => _state != null && _state.CanExecute(ctx.Ctx);

    public override SpecialStateBase GetRuntimeState() => _state;
}
