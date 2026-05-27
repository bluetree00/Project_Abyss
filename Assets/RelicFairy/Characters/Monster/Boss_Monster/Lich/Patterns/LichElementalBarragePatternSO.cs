using UnityEngine;

namespace RelicFairy.Monster
{
/// <summary>
/// 리치 원소 난사 (Elemental Barrage) 패턴 — Phase 1 일반 공격.
///
/// 흐름: 이동 유지 (CircleStrafe 힌트) → 시전(castDuration) → 투사체 3발 연속 발사
///       (각 shotInterval 간격) → 복귀(recoveryDuration)
/// </summary>
[CreateAssetMenu(menuName = "RelicFairy/Boss/Lich/Lich_ElementalBarragePattern", fileName = "Lich_ElementalBarragePattern")]
public class LichElementalBarragePatternSO : BossPatternSO
{
    [Header("Elemental Barrage — Range")]
    [Tooltip("유효 사정거리 (m)")]
    public float maxRange = 30f;

    [Header("Elemental Barrage — Timing")]
    [Tooltip("첫 발사 전 시전 시간 (초)")]
    public float castDuration = 0.6f;
    [Tooltip("발사 간 간격 (초)")]
    public float shotInterval = 0.25f;
    [Tooltip("마지막 발사 후 복귀 시간 (초)")]
    public float recoveryDuration = 0.5f;

    [Header("Elemental Barrage — Shots")]
    [Tooltip("연속 발사 횟수")]
    public int shotCount = 3;

    [Header("Elemental Barrage — Projectile")]
    [Tooltip("투사체 프리팹 (MonsterProjectile 컴포넌트 필요). null이면 즉발 처리.")]
    public GameObject projectilePrefab;
    [Tooltip("투사체 비행 속도 (m/s)")]
    public float projectileSpeed = 16f;
    [Tooltip("투사체 최대 비행 거리 (m)")]
    public float projectileRange = 35f;

    [Header("Elemental Barrage — Damage")]
    [Tooltip("기본 attackPower에 곱할 배율 (발당)")]
    public float damageMultiplier = 0.7f;

    [Header("Elemental Barrage — Cooldown")]
    [Tooltip("패턴 완료 후 재사용 대기 시간 (초)")]
    public float patternCooldown = 5f;

    // ── 런타임 ───────────────────────────────────────────
    private LichElementalBarrageState _state;

    public override void Initialize(BossPatternContext ctx) => _state = new LichElementalBarrageState(this);
    public override void OnRecycled()                       => _state = new LichElementalBarrageState(this);

    public override bool CanExecute(BossPatternContext ctx)
    {
        if (ctx.Ctx.Runtime.PlayerTarget == null) return false;
        var lichBB = (ctx.Boss as LichMonster)?.LichBB;
        if (lichBB != null && lichBB.ElementalBarrageCooldown > 0f) return false;
        return Vector3.Distance(ctx.Ctx.Transform.position, ctx.Ctx.Runtime.PlayerTarget.position) <= maxRange;
    }

    public override SpecialStateBase GetRuntimeState() => _state;
}

// ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
// LichElementalBarrageState — UnInterruptible (이동 자유)
// ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

public class LichElementalBarrageState : UnInterruptibleState<LichElementalBarragePatternSO>
{
    private enum Phase { Cast, Shoot, Recovery }

    private Phase _phase;
    private float _timer;
    private int   _shotsFired;

    public LichElementalBarrageState(LichElementalBarragePatternSO data) : base(data) { }

    public override void Enter(MonsterContext ctx)
    {
        _phase      = Phase.Cast;
        _timer      = 0f;
        _shotsFired = 0;

        ctx.Animator?.CrossFade("ElementalBarrage", 0.1f);

        var mc = (ctx.Monster as LichMonster)?.MovementController;
        mc?.RequestMovementState(LichMovementState.CircleStrafe);
        mc?.SetLocked(true);

        FacePlayer(ctx);
    }

    public override void Update(MonsterContext ctx)
    {
        _timer += Time.deltaTime;

        switch (_phase)
        {
            case Phase.Cast:
                FacePlayer(ctx);
                if (_timer >= Data.castDuration)
                {
                    FireShot(ctx);
                    _shotsFired++;
                    _phase = Phase.Shoot;
                    _timer = 0f;
                }
                break;

            case Phase.Shoot:
                FacePlayer(ctx);
                if (_timer >= Data.shotInterval)
                {
                    if (_shotsFired < Data.shotCount)
                    {
                        FireShot(ctx);
                        _shotsFired++;
                        _timer = 0f;
                    }
                    else
                    {
                        _phase = Phase.Recovery;
                        _timer = 0f;
                    }
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
        (ctx.Monster as LichMonster)?.MovementController?.SetLocked(false);

        var lich = ctx.Monster as LichMonster;
        if (lich?.LichBB != null)
            lich.LichBB.ElementalBarrageCooldown = Data.patternCooldown;
    }

    private void FireShot(MonsterContext ctx)
    {
        if (ctx.Runtime.PlayerTarget == null) return;

        Vector3 origin    = ctx.Transform.position + Vector3.up * 1.5f;
        Vector3 targetPos = ctx.Runtime.PlayerTarget.position + Vector3.up * 1f;
        Vector3 dir       = (targetPos - origin).normalized;

        PatternGuideHelper.Sphere(
            targetPos,
            0.4f,
            PatternGuideHelper.Active,
            lifetime: 0.3f);

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
            float dist = Vector3.Distance(ctx.Transform.position, ctx.Runtime.PlayerTarget.position);
            if (dist > Data.maxRange) return;
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
