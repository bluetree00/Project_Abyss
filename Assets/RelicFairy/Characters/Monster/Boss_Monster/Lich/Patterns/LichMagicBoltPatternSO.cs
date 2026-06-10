using UnityEngine;

namespace RelicFairy.Monster
{
/// <summary>
/// 리치 마법 투사체 (Magic Bolt) 패턴 — Phase 1.
///
/// 흐름: 시전 자세(castDuration) → 투사체 발사 → 복귀(recoveryDuration)
/// projectilePrefab null 시 유효 사정거리 내 즉발 대미지로 폴백.
/// </summary>
[CreateAssetMenu(menuName = "RelicFairy/Boss/Lich/Lich_MagicBoltPattern", fileName = "Lich_MagicBoltPattern")]
public class LichMagicBoltPatternSO : BossPatternSO
{
    [Header("Magic Bolt — Range")]
    [Tooltip("유효 사정거리 (m)")]
    public float maxRange = 25f;

    [Header("Magic Bolt — Timing")]
    [Tooltip("시전 자세 유지 시간 (초)")]
    public float castDuration = 0.8f;
    [Tooltip("투사체 발사 후 복귀 대기 시간 (초)")]
    public float recoveryDuration = 0.5f;

    [Header("Magic Bolt — Projectile")]
    [Tooltip("투사체 프리팹 (MonsterProjectile 컴포넌트 필요). null이면 즉발 대미지 처리.")]
    public GameObject projectilePrefab;
    [Tooltip("투사체 비행 속도 (m/s)")]
    public float projectileSpeed = 14f;
    [Tooltip("투사체 최대 비행 거리 (m)")]
    public float projectileRange = 30f;

    [Header("Magic Bolt — Damage")]
    [Tooltip("기본 attackPower에 곱할 배율")]
    public float damageMultiplier = 1.1f;

    [Header("Magic Bolt — Cooldown")]
    [Tooltip("패턴 완료 후 재사용 대기 시간 (초)")]
    public float patternCooldown = 4f;

    [Header("Magic Bolt — Telegraph")]
    [Tooltip("캐스트 중 표시할 발사 경로 빔 너비 (m)")]
    public float trajectoryBeamWidth = 0.12f;

    // ── 런타임 ───────────────────────────────────────────
    private LichMagicBoltState _state;

    public override void Initialize(BossPatternContext ctx) => _state = new LichMagicBoltState(this);
    public override void OnRecycled()                       => _state = new LichMagicBoltState(this);

    public override bool CanExecute(BossPatternContext ctx)
    {
        if (ctx.Ctx.Runtime.PlayerTarget == null) return false;
        var lichBB = (ctx.Boss as LichMonster)?.LichBB;
        if (lichBB != null && lichBB.MagicBoltCooldown > 0f) return false;
        return Vector3.Distance(ctx.Ctx.Transform.position, ctx.Ctx.Runtime.PlayerTarget.position) <= maxRange;
    }

    public override SpecialStateBase GetRuntimeState() => _state;
}

// ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
// LichMagicBoltState — MovementLocked (보스 고정, 중단 가능)
// ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

public class LichMagicBoltState : UnInterruptibleState<LichMagicBoltPatternSO>
{
    private enum Phase { Cast, Recovery }

    private Phase      _phase;
    private float      _timer;
    private bool       _fired;
    private Vector3    _lockedTargetPos;  // Enter 시점 조준 위치 — 가이드·발사 모두 이 위치 사용
    private GameObject _castGuide;
    private GameObject _beamGuide;

    public LichMagicBoltState(LichMagicBoltPatternSO data) : base(data) { }

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

        // 조준 위치를 Enter 시점에 고정 — 이후 플레이어가 이동해도 발사 방향 불변
        _lockedTargetPos = ctx.Runtime.PlayerTarget != null
            ? ctx.Runtime.PlayerTarget.position
            : ctx.Transform.position + ctx.Transform.forward * 10f;

        // 착탄 예고 disc (노란색)
        _castGuide = PatternGuideHelper.Disc(_lockedTargetPos, 1.0f, PatternGuideHelper.Telegraph);

        // 발사 경로 빔 (보스 → 착탄 지점)
        Vector3 origin   = ctx.Transform.position + Vector3.up * 1.5f;
        Vector3 aimPoint = _lockedTargetPos + Vector3.up * 1f;
        Vector3 toTarget = aimPoint - origin;
        float   aimDist  = toTarget.magnitude;
        if (aimDist > 0.1f)
            _beamGuide = PatternGuideHelper.Beam(
                origin, toTarget.normalized, aimDist * 0.92f,
                Data.trajectoryBeamWidth, PatternGuideHelper.Telegraph);
    }

    public override void Update(MonsterContext ctx)
    {
        _timer += Time.deltaTime;

        if (_phase == Phase.Cast)
        {
            FacePlayer(ctx);

            if (_timer >= Data.castDuration)
            {
                PatternGuideHelper.SafeDestroy(ref _castGuide);
                PatternGuideHelper.SafeDestroy(ref _beamGuide);
                FireProjectile(ctx);
                _phase = Phase.Recovery;
                _timer = 0f;
            }
        }
        else
        {
            if (_timer >= Data.recoveryDuration)
                ctx.Monster.ChangeState<ChaseState>();
        }
    }

    public override void Exit(MonsterContext ctx)
    {
        PatternGuideHelper.SafeDestroy(ref _castGuide);
        PatternGuideHelper.SafeDestroy(ref _beamGuide);
        (ctx.Monster as LichMonster)?.MovementController?.SetLocked(false);

        var lich = ctx.Monster as LichMonster;
        if (lich?.LichBB != null)
            lich.LichBB.MagicBoltCooldown = Data.patternCooldown;
    }

    private void FireProjectile(MonsterContext ctx)
    {
        if (_fired) return;
        _fired = true;

        // 착탄 지점 Active 가이드 (빨간색, 0.4s) — 고정 위치
        PatternGuideHelper.Sphere(
            _lockedTargetPos + Vector3.up * 1f,
            0.5f,
            PatternGuideHelper.Active,
            lifetime: 0.4f);

        Vector3 origin    = ctx.Transform.position + Vector3.up * 1.5f;
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
            // 즉발 폴백: Enter 시점 사정거리 내에 있었으면 데미지
            float dist = Vector3.Distance(ctx.Transform.position, _lockedTargetPos);
            if (dist > Data.maxRange) return;
            if (ctx.Runtime.PlayerTarget == null) return;

            var player = ctx.Runtime.PlayerTarget.GetComponent<PlayerController>();
            if (player == null) return;

            int dmg = Mathf.Max(1, (int)(ctx.Config.stat.attackPower * Data.damageMultiplier));
            player.TakeDamage(dmg);
        }
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
