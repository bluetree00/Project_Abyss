using Abyss.Monster;
using UnityEngine;

/// <summary>LeapSlam 패턴 SO. 점프 내려찍기 데이터 + BKLeapSlamState 소유.</summary>
[CreateAssetMenu(fileName = "BK_Pattern_LeapSlam",
                 menuName  = "Abyss/Boss/BlackKnight/Patterns/LeapSlam")]
public class BKLeapSlamPatternSO : BossPatternSO
{
    // ── 사운드 ────────────────────────────────────────────
    [Header("사운드")]
    public AudioClip leapJumpSfx;
    public AudioClip leapLandSfx;

    // ── LeapSlam 데이터 ───────────────────────────────────
    [Header("LeapSlam")]
    public string    leapAnimState        = "Attack03";
    public float     leapCooldown         = 18f;
    [Tooltip("착지 원이 플레이어를 추적하는 시간 (초)")]
    public float     leapTrackDuration    = 2.5f;
    [Tooltip("원 고정 후 착지까지 시간 (초)")]
    public float     leapLockDuration     = 1.2f;
    [Tooltip("착지 피해 반경 (m)")]
    public float     leapSlamRadius       = 5.0f;
    public float     leapDamageMul        = 2.5f;
    [Tooltip("보스가 사라지는 점프 높이 (m)")]
    public float     leapJumpHeight       = 20f;
    [Tooltip("도약 올라가는 시간 (초)")]
    public float     leapUpDuration       = 0.3f;
    [Tooltip("착지 후 경직 시간 (초)")]
    public float     leapRecoveryDuration = 0.5f;
    [Tooltip("착지 VFX 표시 시간 (초)")]
    public float     leapVfxDuration      = 2.0f;
    public float     leapKnockbackY       = 0.5f;
    public float     leapKnockbackMul     = 2.0f;
    [Tooltip("착지 충격파 VFX 프리팹 (비워두면 스킵)")]
    public GameObject leapSlamVfxPrefab;

    // ── 런타임 ────────────────────────────────────────────
    [System.NonSerialized] private BKLeapSlamState _state;
    [System.NonSerialized] private BKEffectPool    _vfxPool;

    public override void Initialize(BossPatternContext ctx)
    {
        _state = new BKLeapSlamState(this, ctx.Blackboard);

        if (leapSlamVfxPrefab != null)
        {
            var cont = new UnityEngine.GameObject("[LeapVfxPool]");
            cont.transform.SetParent(ctx.Ctx.Monster.transform, false);
            _vfxPool = new BKEffectPool(leapSlamVfxPrefab, 4, cont.transform);
            _state.SetVfxPool(_vfxPool);
        }
    }

    public override void OnRecycled()
    {
        _vfxPool?.RecycleAll();
    }

    public override void Dispose()
    {
        _vfxPool?.Dispose();
        _vfxPool = null;
    }

    public override bool CanExecute(BossPatternContext ctx)
        => _state != null && _state.CanExecute(ctx.Ctx);

    public override SpecialStateBase GetRuntimeState() => _state;
}
