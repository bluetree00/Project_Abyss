using Abyss.Monster;
using UnityEngine;

/// <summary>ScatterShot 패턴 SO. 부채꼴 투사체 데이터 + BKScatterShotState + 투사체 풀 소유.</summary>
[CreateAssetMenu(fileName = "BK_Pattern_ScatterShot",
                 menuName  = "Abyss/Boss/BlackKnight/Patterns/ScatterShot")]
public class BKScatterShotPatternSO : BossPatternSO
{
    // ── 사운드 ────────────────────────────────────────────
    [Header("사운드")]
    public AudioClip scatterSfx;

    // ── ScatterShot 데이터 ────────────────────────────────
    [Header("ScatterShot")]
    public string    scatterAnimState   = "Attack01";
    public float     scatterCooldown    = 10f;
    [Tooltip("부채꼴 내 투사체 개수")]
    public int       scatterCount       = 7;
    [Tooltip("부채꼴 전체 각도 (도)")]
    public float     scatterFanAngle    = 60f;
    [Tooltip("2회차 발사 시 부채꼴 회전 오프셋 (도)")]
    public float     scatterAngleOffset = 15f;
    public float     scatterSpeed       = 12f;
    public float     scatterRange       = 18f;
    public float     scatterDamageMul   = 0.8f;
    [Tooltip("1 → 2회차 발사 사이 딜레이 (초)")]
    public float     scatterShotDelay   = 0.9f;
    [Tooltip("투사체 충돌 판정 반경 (m)")]
    public float     scatterHitRadius   = 0.25f;
    [Tooltip("투사체 프리팹 (BKBossProjectile 필요) — 비워두면 폴백 구체 사용")]
    public GameObject scatterProjectilePrefab;

    // ── 런타임 ────────────────────────────────────────────
    [System.NonSerialized] private BKScatterShotState _state;
    [System.NonSerialized] private BKProjectilePool   _pool1;
    [System.NonSerialized] private BKProjectilePool   _pool2;

    public override void Initialize(BossPatternContext ctx)
    {
        _state = new BKScatterShotState(this, ctx.Blackboard);

        if (scatterProjectilePrefab != null)
        {
            var parent = ctx.Ctx.Monster.transform;
            var c1 = new UnityEngine.GameObject("[ProjectilePool1]");
            var c2 = new UnityEngine.GameObject("[ProjectilePool2]");
            c1.transform.SetParent(parent, false);
            c2.transform.SetParent(parent, false);
            _pool1 = new BKProjectilePool(scatterProjectilePrefab, 10, c1.transform);
            _pool2 = new BKProjectilePool(scatterProjectilePrefab, 10, c2.transform);
            _state.SetPools(_pool1, _pool2);
        }
    }

    public override void OnRecycled()
    {
        _pool1?.RecycleAll();
        _pool2?.RecycleAll();
    }

    public override void Dispose()
    {
        _pool1?.Dispose();
        _pool2?.Dispose();
        _pool1 = _pool2 = null;
    }

    public override bool CanExecute(BossPatternContext ctx)
        => _state != null && _state.CanExecute(ctx.Ctx);

    public override SpecialStateBase GetRuntimeState() => _state;
}
