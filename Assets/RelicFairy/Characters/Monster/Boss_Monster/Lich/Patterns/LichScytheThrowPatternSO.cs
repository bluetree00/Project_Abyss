using UnityEngine;

namespace RelicFairy.Monster
{
/// <summary>
/// 리치 낫 던지기 (Scythe Throw) 패턴 — Phase 2 일반 공격.
///
/// 흐름: 이동 유지 (CircleStrafe 힌트) → 시전(castDuration) → 부메랑 낫 발사
///       → 투사체가 maxRange 도달 후 귀환 or 복귀(recoveryDuration)
/// 투사체 프리팹 null 시 즉발 대미지로 폴백.
/// </summary>
[CreateAssetMenu(menuName = "RelicFairy/Boss/Lich/Lich_ScytheThrowPattern", fileName = "Lich_ScytheThrowPattern")]
public class LichScytheThrowPatternSO : BossPatternSO
{
    [Header("Scythe Throw — Range")]
    [Tooltip("유효 사정거리 (m)")]
    public float maxRange = 28f;

    [Header("Scythe Throw — Timing")]
    [Tooltip("시전 자세 유지 시간 (초)")]
    public float castDuration = 0.7f;
    [Tooltip("발사 후 복귀 대기 시간 (초)")]
    public float recoveryDuration = 0.8f;

    [Header("Scythe Throw — Projectile")]
    [Tooltip("낫 투사체 프리팹 (MonsterProjectile 컴포넌트 필요). null이면 즉발 처리.")]
    public GameObject projectilePrefab;
    [Tooltip("낫 비행 속도 (m/s)")]
    public float projectileSpeed = 18f;
    [Tooltip("낫 최대 비행 거리 (m)")]
    public float projectileRange = 32f;

    [Header("Scythe Throw — Damage")]
    [Tooltip("기본 attackPower에 곱할 배율")]
    public float damageMultiplier = 1.3f;

    [Header("Scythe Throw — Cooldown")]
    [Tooltip("패턴 완료 후 재사용 대기 시간 (초)")]
    public float patternCooldown = 8f;

    [Header("Scythe Throw — Telegraph")]
    [Tooltip("투척 경로 빔 너비 (m)")]
    public float trajectoryBeamWidth = 0.15f;

    // ── 런타임 ───────────────────────────────────────────
    private LichScytheThrowState _state;

    public override void Initialize(BossPatternContext ctx) => _state = new LichScytheThrowState(this);
    public override void OnRecycled()                       => _state = new LichScytheThrowState(this);

    public override bool CanExecute(BossPatternContext ctx)
    {
        if (ctx.Ctx.Runtime.PlayerTarget == null) return false;
        var lichBB = (ctx.Boss as LichMonster)?.LichBB;
        if (lichBB == null || !lichBB.IsPhase2) return false;
        if (lichBB.ScytheThrowCooldown > 0f) return false;
        return Vector3.Distance(ctx.Ctx.Transform.position, ctx.Ctx.Runtime.PlayerTarget.position) <= maxRange;
    }

    public override SpecialStateBase GetRuntimeState() => _state;
}

// ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
// LichScytheThrowState — UnInterruptible (이동 자유)
// ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

public class LichScytheThrowState : UnInterruptibleState<LichScytheThrowPatternSO>
{
    private enum Phase { Cast, Recovery }

    private Phase      _phase;
    private float      _timer;
    private bool       _thrown;
    private Vector3    _lockedTargetPos;
    private GameObject _castGuide;
    private GameObject _beamGuide;

    public LichScytheThrowState(LichScytheThrowPatternSO data) : base(data) { }

    public override void Enter(MonsterContext ctx)
    {
        _phase  = Phase.Cast;
        _timer  = 0f;
        _thrown = false;

        ctx.Animator?.CrossFade("ScytheThrow", 0.1f);

        var mc = (ctx.Monster as LichMonster)?.MovementController;
        mc?.RequestMovementState(LichMovementState.CircleStrafe);
        mc?.SetLocked(true);

        FacePlayer(ctx);

        // 조준 위치 Enter 시점 고정
        _lockedTargetPos = ctx.Runtime.PlayerTarget != null
            ? ctx.Runtime.PlayerTarget.position
            : ctx.Transform.position + ctx.Transform.forward * 10f;

        // 착탄 Sphere 가이드 (노란색)
        _castGuide = PatternGuideHelper.Sphere(
            _lockedTargetPos + Vector3.up * 0.8f, 0.6f, PatternGuideHelper.Telegraph);

        // 투척 경로 빔 가이드
        Vector3 origin   = ctx.Transform.position + Vector3.up * 1.2f;
        Vector3 aimPoint = _lockedTargetPos + Vector3.up * 0.8f;
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
                if (!_thrown)
                {
                    _thrown = true;
                    PatternGuideHelper.SafeDestroy(ref _castGuide);
                    PatternGuideHelper.SafeDestroy(ref _beamGuide);
                    Throw(ctx);
                }
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
            lich.LichBB.ScytheThrowCooldown = Data.patternCooldown;
    }

    private void Throw(MonsterContext ctx)
    {
        Vector3 origin    = ctx.Transform.position + Vector3.up * 1.2f;
        Vector3 targetPos = _lockedTargetPos + Vector3.up * 0.8f;
        Vector3 dir       = (targetPos - origin).normalized;

        // 착탄 지점 Active 가이드 (빨간색, 0.5s)
        PatternGuideHelper.Sphere(targetPos, 0.6f, PatternGuideHelper.Active, lifetime: 0.5f);

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
