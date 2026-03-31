using Abyss.Monster;
using UnityEngine;

/// <summary>RainAttack 패턴 SO. 낙하 공격 데이터 + BKRainAttackState 소유 + 풀 관리.</summary>
[CreateAssetMenu(fileName = "BK_Pattern_RainAttack",
                 menuName  = "Abyss/Boss/BlackKnight/Patterns/RainAttack")]
public class BKRainAttackPatternSO : BossPatternSO
{
    // ── 사운드 ────────────────────────────────────────────
    [Header("사운드")]
    public AudioClip rainSfx;

    // ── 방어막 ────────────────────────────────────────────
    [Header("RainAttack 방어막 (무적 시각 효과)")]
    public float barrierRadius      = 2.5f;
    public Color barrierColor       = new Color(0.3f, 0.7f, 1f, 0.22f);
    public Color barrierPeakColor   = new Color(0.5f, 0.9f, 1f, 0.38f);
    public float barrierPulsePeriod = 1.2f;

    // ── RainAttack 데이터 ─────────────────────────────────
    [Header("RainAttack")]
    public string    rainAnimState          = "Attack02";
    public float     rainCooldown           = 14f;
    [Tooltip("반복 라운드 수")]
    public int       rainRounds             = 2;
    [Tooltip("라운드당 낙하 횟수")]
    public int       rainDropsPerRound      = 3;
    [Tooltip("경고 원 표시 시간 (초)")]
    public float     rainWarningTime        = 0.9f;
    [Tooltip("경고 시간 중 플레이어 추적 비율 (0~1)")]
    public float     rainTrackRatio         = 0.70f;
    [Tooltip("같은 라운드 내 연속 낙하 간격 (초)")]
    public float     rainDropInterval       = 0.05f;
    [Tooltip("라운드 사이 대기 시간 (초)")]
    public float     rainRoundDelay         = 1.2f;
    [Tooltip("낙하 피해 범위 반경 (m)")]
    public float     rainHitRadius          = 2.0f;
    public float     rainDamageMul          = 1.0f;
    public float     rainMeteorSpawnHeight  = 12f;
    [Tooltip("메인 패턴 완료 후 Chase 복귀 전 대기 시간 (초)")]
    public float     rainCooldownDelay      = 3.0f;
    public float     rainBarrierFadeDuration = 1.5f;
    public float     rainHitVfxDuration     = 3.0f;
    [Tooltip("낙하물 VFX 프리팹 — 비워두면 스킵")]
    public GameObject rainMeteorVfxPrefab;
    [Tooltip("착지 VFX 프리팹 — 비워두면 스킵")]
    public GameObject rainHitVfxPrefab;

    [Header("RainAttack Phase 1 (랜덤 동시 낙하)")]
    public float rainRandomRadiusMin    = 2f;
    public float rainRandomRadius       = 5f;
    public int   rainPhase1Count        = 3;
    public int   rainPhase2Count        = 5;
    public float rainRandomStaggerSpread = 1.5f;
    public float rainRandomMinInterval  = 1.2f;
    public float rainRandomMaxInterval  = 2.5f;
    public float rainConcurrentInterval = 1.8f;

    [Header("RainAttack Phase 2 (+/X 패턴 동시 낙하)")]
    public int   rainPatternCount   = 4;
    public float rainPatternSpacing = 3f;
    public float rainPatternRadius  = 3f;

    // ── 런타임 ────────────────────────────────────────────
    [System.NonSerialized] private BKRainAttackState _state;
    [System.NonSerialized] private BKEffectPool      _meteorPool;
    [System.NonSerialized] private BKEffectPool      _hitVfxPool;
    [System.NonSerialized] private BKGroundCirclePool _circlePool;

    public override void Initialize(BossPatternContext ctx)
    {
        var parent = ctx.Ctx.Monster.transform;

        var meteorCont = new UnityEngine.GameObject("[MeteorPool]");
        var hitVfxCont = new UnityEngine.GameObject("[HitVfxPool]");
        var circleCont = new UnityEngine.GameObject("[GroundCirclePool]");
        meteorCont.transform.SetParent(parent, false);
        hitVfxCont.transform.SetParent(parent, false);
        circleCont.transform.SetParent(parent, false);

        if (rainMeteorVfxPrefab != null)
            _meteorPool = new BKEffectPool(rainMeteorVfxPrefab, 16, meteorCont.transform);
        if (rainHitVfxPrefab != null)
            _hitVfxPool = new BKEffectPool(rainHitVfxPrefab, 16, hitVfxCont.transform);
        _circlePool = new BKGroundCirclePool(24, circleCont.transform);

        _state = new BKRainAttackState(this, ctx.Blackboard);
        var dropCtx = new BKDropContext(
            _meteorPool, _hitVfxPool, _circlePool,
            rainMeteorVfxPrefab, rainHitVfxPrefab,
            parent);
        _state.SetDropContext(dropCtx);
    }

    public override void OnRecycled()
    {
        _meteorPool?.RecycleAll();
        _hitVfxPool?.RecycleAll();
        _circlePool?.RecycleAll();
    }

    public override void Dispose()
    {
        _meteorPool?.Dispose();
        _hitVfxPool?.Dispose();
        _circlePool?.Dispose();
        _meteorPool = _hitVfxPool = null;
        _circlePool = null;
    }

    public override bool CanExecute(BossPatternContext ctx)
        => _state != null && _state.CanExecute(ctx.Ctx);

    public override SpecialStateBase GetRuntimeState() => _state;
}
