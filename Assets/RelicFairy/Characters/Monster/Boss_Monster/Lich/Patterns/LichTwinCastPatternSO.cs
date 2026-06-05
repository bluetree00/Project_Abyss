using UnityEngine;

namespace RelicFairy.Monster
{
/// <summary>
/// 리치 이중 영창 (Twin Cast) 패턴 — Phase 1 심리전(페인트).
///
/// 흐름: 차징(castDuration) → 페이크 점멸(fakeFlashDuration, 빨강이지만 발사 안 함)
///       → 미끼 구간(feintGapDuration, 노랑 복귀) → 진짜 발사(빨강 + 투사체) → 복귀
///
/// UX: 첫 빨강(페이크)에 패닉 회피한 플레이어는 i-frame이 끝난 뒤 진짜 발사에 맞는다.
///     "진짜 발사 신호(투사체)에 맞춰 회피"를 학습시키는 페인트.
///     조준은 Enter에 고정 → 텔레그래프 라인은 정직하며, 속임수는 '타이밍'에만 있다.
///
/// 애니: MagicBolt 클립 재사용(차징 + 진짜 발사 시 재시전 제스처). 신규 Animator 상태 없음.
/// </summary>
[CreateAssetMenu(menuName = "RelicFairy/Boss/Lich/Lich_TwinCastPattern", fileName = "Lich_TwinCastPattern")]
public class LichTwinCastPatternSO : BossPatternSO
{
    [Header("Twin Cast — Range")]
    [Tooltip("유효 사정거리 (m)")]
    public float maxRange = 25f;

    [Header("Twin Cast — Timing")]
    [Tooltip("최초 차징(노랑 예고) 시간 (초)")]
    public float castDuration = 0.7f;
    [Tooltip("페이크 빨강 점멸 시간 (초) — 발사하지 않는다")]
    public float fakeFlashDuration = 0.25f;
    [Tooltip("페이크 후 노랑 복귀(미끼) 시간 (초). 길수록 페인트가 강함.")]
    public float feintGapDuration = 0.45f;
    [Tooltip("발사 후 복귀 대기 시간 (초)")]
    public float recoveryDuration = 0.6f;

    [Header("Twin Cast — Projectile")]
    [Tooltip("투사체 프리팹 (MonsterProjectile 필요). null이면 즉발 처리.")]
    public GameObject projectilePrefab;
    [Tooltip("투사체 비행 속도 (m/s)")]
    public float projectileSpeed = 14f;
    [Tooltip("투사체 최대 비행 거리 (m)")]
    public float projectileRange = 30f;

    [Header("Twin Cast — Damage")]
    [Tooltip("기본 attackPower에 곱할 배율")]
    public float damageMultiplier = 1.1f;

    [Header("Twin Cast — Telegraph")]
    [Tooltip("발사 경로 빔 너비 (m)")]
    public float trajectoryBeamWidth = 0.12f;

    [Header("Twin Cast — VFX 훅 (추후 가이드에 맞춰 적용)")]
    [Tooltip("차징~발사 동안 보스 시전 지점에 표시할 VFX. null이면 미사용.")]
    public GameObject castVfxPrefab;

    [Header("Twin Cast — Cooldown")]
    [Tooltip("패턴 완료 후 재사용 대기 시간 (초)")]
    public float patternCooldown = 7f;

    // ── 런타임 ───────────────────────────────────────────
    private LichTwinCastState _state;

    public override void Initialize(BossPatternContext ctx) => _state = new LichTwinCastState(this);
    public override void OnRecycled()                       => _state = new LichTwinCastState(this);

    public override bool CanExecute(BossPatternContext ctx)
    {
        if (ctx.Ctx.Runtime.PlayerTarget == null) return false;
        var lichBB = (ctx.Boss as LichMonster)?.LichBB;
        if (lichBB != null && lichBB.TwinCastCooldown > 0f) return false;
        return Vector3.Distance(ctx.Ctx.Transform.position, ctx.Ctx.Runtime.PlayerTarget.position) <= maxRange;
    }

    public override SpecialStateBase GetRuntimeState() => _state;
}

// ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
// LichTwinCastState — UnInterruptible (보스 고정, 중단 불가)
// ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

public class LichTwinCastState : UnInterruptibleState<LichTwinCastPatternSO>
{
    private enum Phase { Cast, Fake, Gap, Recovery }

    private Phase      _phase;
    private float      _timer;
    private bool       _fired;
    private Vector3    _lockedTargetPos;
    private GameObject _castGuide;
    private GameObject _beamGuide;
    private GameObject _castVfx;

    public LichTwinCastState(LichTwinCastPatternSO data) : base(data) { }

    public override void Enter(MonsterContext ctx)
    {
        _phase = Phase.Cast;
        _timer = 0f;
        _fired = false;

        ctx.Animator?.CrossFade("MagicBolt", 0.1f);

        var mc = (ctx.Monster as LichMonster)?.MovementController;
        mc?.RequestMovementState(LichMovementState.IdleHover);
        mc?.SetLocked(true);

        FacePlayer(ctx);

        // 조준 위치를 Enter 시점에 고정 — 이후 플레이어 이동과 무관(텔레그래프 라인 정직)
        _lockedTargetPos = ctx.Runtime.PlayerTarget != null
            ? ctx.Runtime.PlayerTarget.position
            : ctx.Transform.position + ctx.Transform.forward * 10f;

        _castGuide = PatternGuideHelper.Disc(_lockedTargetPos, 1.0f, PatternGuideHelper.Telegraph);
        CreateBeam(ctx);

        if (Data.castVfxPrefab != null && _castVfx == null)
            _castVfx = Object.Instantiate(Data.castVfxPrefab, CastPoint(ctx), ctx.Transform.rotation);
    }

    public override void Update(MonsterContext ctx)
    {
        _timer += Time.deltaTime;
        UpdateCastVfx(ctx);

        switch (_phase)
        {
            case Phase.Cast:
                FacePlayer(ctx);
                if (_timer >= Data.castDuration) ToFake();
                break;

            case Phase.Fake:
                // 페이크 빨강 — 발사하지 않는다. 패닉 회피 유도.
                if (_timer >= Data.fakeFlashDuration) ToGap();
                break;

            case Phase.Gap:
                // 노랑 복귀(미끼). 끝나는 순간 진짜 발사.
                if (_timer >= Data.feintGapDuration)
                {
                    FireNow(ctx);
                    _phase = Phase.Recovery;
                    _timer = 0f;
                }
                break;

            case Phase.Recovery:
                if (_timer >= Data.recoveryDuration)
                    ctx.Monster.ChangeState<ChaseState>();
                break;
        }
    }

    public override void Exit(MonsterContext ctx)
    {
        PatternGuideHelper.SafeDestroy(ref _castGuide);
        PatternGuideHelper.SafeDestroy(ref _beamGuide);
        DestroyVfx(ref _castVfx);
        (ctx.Monster as LichMonster)?.MovementController?.SetLocked(false);

        var lich = ctx.Monster as LichMonster;
        if (lich?.LichBB != null)
            lich.LichBB.TwinCastCooldown = Data.patternCooldown;
    }

    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    // 단계 전이
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

    private void ToFake()
    {
        _phase = Phase.Fake;
        _timer = 0f;
        PatternGuideHelper.SetColor(_castGuide, PatternGuideHelper.Active);
        PatternGuideHelper.SetColor(_beamGuide, PatternGuideHelper.Active);
    }

    private void ToGap()
    {
        _phase = Phase.Gap;
        _timer = 0f;
        PatternGuideHelper.SetColor(_castGuide, PatternGuideHelper.Telegraph);
        PatternGuideHelper.SetColor(_beamGuide, PatternGuideHelper.Telegraph);
    }

    private void FireNow(MonsterContext ctx)
    {
        // 진짜 발사 — 재시전 제스처 + 투사체. 텔레그래프 가이드는 즉시 제거.
        ctx.Animator?.CrossFade("MagicBolt", 0.05f);
        PatternGuideHelper.SafeDestroy(ref _castGuide);
        PatternGuideHelper.SafeDestroy(ref _beamGuide);
        FireProjectile(ctx);
    }

    private void FireProjectile(MonsterContext ctx)
    {
        if (_fired) return;
        _fired = true;

        // 착탄 지점 Active 표시 (빨강, 0.4s) — 진짜 발사 신호
        PatternGuideHelper.Sphere(_lockedTargetPos + Vector3.up * 1f, 0.5f, PatternGuideHelper.Active, lifetime: 0.4f);

        Vector3 origin    = CastPoint(ctx);
        Vector3 targetPos = _lockedTargetPos + Vector3.up * 1f;
        Vector3 dir       = (targetPos - origin).normalized;

        if (Data.projectilePrefab != null)
        {
            var go = Object.Instantiate(Data.projectilePrefab, origin, Quaternion.LookRotation(dir));
            if (go.TryGetComponent<MonsterProjectile>(out var proj))
            {
                int dmg = Mathf.Max(1, (int)(ctx.Config.stat.attackPower * Data.damageMultiplier));
                proj.Init(dir, Data.projectileSpeed, Data.projectileRange, dmg, ctx.Config.stat.knockbackForce);
            }
        }
        else
        {
            // 즉발 폴백: Enter 조준 라인 사정거리 내였으면 데미지
            if (ctx.Runtime.PlayerTarget == null) return;
            float dist = Vector3.Distance(ctx.Transform.position, _lockedTargetPos);
            if (dist > Data.maxRange) return;
            var player = ctx.Runtime.PlayerTarget.GetComponent<PlayerController>();
            if (player == null) return;
            int dmg = Mathf.Max(1, (int)(ctx.Config.stat.attackPower * Data.damageMultiplier));
            player.TakeDamage(dmg);
        }
    }

    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    // 헬퍼
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

    private void CreateBeam(MonsterContext ctx)
    {
        Vector3 origin   = CastPoint(ctx);
        Vector3 aimPoint = _lockedTargetPos + Vector3.up * 1f;
        Vector3 toTarget = aimPoint - origin;
        float   aimDist  = toTarget.magnitude;
        if (aimDist > 0.1f)
            _beamGuide = PatternGuideHelper.Beam(
                origin, toTarget.normalized, aimDist * 0.92f,
                Data.trajectoryBeamWidth, PatternGuideHelper.Telegraph);
    }

    private static Vector3 CastPoint(MonsterContext ctx) => ctx.Transform.position + Vector3.up * 1.5f;

    private void UpdateCastVfx(MonsterContext ctx)
    {
        if (_castVfx == null) return;
        _castVfx.transform.position = CastPoint(ctx);
        _castVfx.transform.rotation = ctx.Transform.rotation;
    }

    private static void DestroyVfx(ref GameObject go)
    {
        if (go == null) return;
        Object.Destroy(go);
        go = null;
    }

    private static void FacePlayer(MonsterContext ctx)
    {
        if (ctx.Runtime.PlayerTarget == null) return;
        Vector3 dir = ctx.Runtime.PlayerTarget.position - ctx.Transform.position;
        dir.y = 0f;
        if (dir.sqrMagnitude > 0.001f)
            ctx.Transform.rotation = Quaternion.LookRotation(dir);
    }
}
}
