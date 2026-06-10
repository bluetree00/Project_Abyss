using UnityEngine;

namespace RelicFairy.Monster
{
/// <summary>
/// 리치 비전 구체 (Arcane Orb) 패턴 — Phase 1 일반 공격.
///
/// 흐름: 이동 유지 (AdvanceFloat 힌트) → 시전(castDuration) → 3방향 구체 동시 발사
///       → 복귀(recoveryDuration)
/// 구체는 보스 정면 기준 -spreadAngle, 0, +spreadAngle 방향으로 발사.
/// </summary>
[CreateAssetMenu(menuName = "RelicFairy/Boss/Lich/Lich_ArcaneOrbPattern", fileName = "Lich_ArcaneOrbPattern")]
public class LichArcaneOrbPatternSO : BossPatternSO
{
    [Header("Arcane Orb — Range")]
    [Tooltip("유효 사정거리 (m)")]
    public float maxRange = 25f;

    [Header("Arcane Orb — Timing")]
    [Tooltip("시전 자세 유지 시간 (초)")]
    public float castDuration = 0.9f;
    [Tooltip("발사 후 복귀 대기 시간 (초)")]
    public float recoveryDuration = 0.6f;

    [Header("Arcane Orb — Spread")]
    [Tooltip("좌우 구체의 확산 각도 (도)")]
    public float spreadAngle = 25f;
    [Tooltip("발사 구체 수 (홀수 권장, 중앙 포함)")]
    public int orbCount = 3;

    [Header("Arcane Orb — Projectile")]
    [Tooltip("투사체 프리팹 (MonsterProjectile 컴포넌트 필요). null이면 즉발 처리.")]
    public GameObject projectilePrefab;
    [Tooltip("투사체 비행 속도 (m/s)")]
    public float projectileSpeed = 10f;
    [Tooltip("투사체 최대 비행 거리 (m)")]
    public float projectileRange = 28f;

    [Header("Arcane Orb — Damage")]
    [Tooltip("기본 attackPower에 곱할 배율 (구체당)")]
    public float damageMultiplier = 0.9f;

    [Header("Arcane Orb — Cooldown")]
    [Tooltip("패턴 완료 후 재사용 대기 시간 (초)")]
    public float patternCooldown = 6f;

    // ── 런타임 ───────────────────────────────────────────
    private LichArcaneOrbState _state;

    public override void Initialize(BossPatternContext ctx) => _state = new LichArcaneOrbState(this);
    public override void OnRecycled()                       => _state = new LichArcaneOrbState(this);

    public override bool CanExecute(BossPatternContext ctx)
    {
        if (ctx.Ctx.Runtime.PlayerTarget == null) return false;
        var lichBB = (ctx.Boss as LichMonster)?.LichBB;
        if (lichBB != null && lichBB.ArcaneOrbCooldown > 0f) return false;
        return Vector3.Distance(ctx.Ctx.Transform.position, ctx.Ctx.Runtime.PlayerTarget.position) <= maxRange;
    }

    public override SpecialStateBase GetRuntimeState() => _state;
}

// ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
// LichArcaneOrbState — UnInterruptible (이동 자유)
// ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

public class LichArcaneOrbState : UnInterruptibleState<LichArcaneOrbPatternSO>
{
    private enum Phase { Cast, Recovery }

    private Phase _phase;
    private float _timer;
    private bool  _fired;

    public LichArcaneOrbState(LichArcaneOrbPatternSO data) : base(data) { }

    public override void Enter(MonsterContext ctx)
    {
        _phase = Phase.Cast;
        _timer = 0f;
        _fired = false;

        ctx.Animator?.CrossFade("ArcaneOrb", 0.1f);

        var mc = (ctx.Monster as LichMonster)?.MovementController;
        mc?.RequestMovementState(LichMovementState.AdvanceFloat);
        mc?.SetLocked(true);

        FacePlayer(ctx);
    }

    public override void Update(MonsterContext ctx)
    {
        _timer += Time.deltaTime;

        if (_phase == Phase.Cast)
        {
            FacePlayer(ctx);

            if (_timer >= Data.castDuration)
            {
                if (!_fired)
                {
                    _fired = true;
                    FireOrbs(ctx);
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
        (ctx.Monster as LichMonster)?.MovementController?.SetLocked(false);

        var lich = ctx.Monster as LichMonster;
        if (lich?.LichBB != null)
            lich.LichBB.ArcaneOrbCooldown = Data.patternCooldown;
    }

    private void FireOrbs(MonsterContext ctx)
    {
        if (ctx.Runtime.PlayerTarget == null) return;

        Vector3 origin    = ctx.Transform.position + Vector3.up * 1.5f;
        Vector3 targetPos = ctx.Runtime.PlayerTarget.position + Vector3.up * 1f;
        Vector3 baseDir   = (targetPos - origin).normalized;

        int count = Mathf.Max(1, Data.orbCount);
        // 각도 스텝: orbCount=3 → [-spread, 0, +spread]
        float step = count > 1 ? Data.spreadAngle * 2f / (count - 1) : 0f;
        float startAngle = count > 1 ? -Data.spreadAngle : 0f;

        for (int i = 0; i < count; i++)
        {
            float angle = startAngle + step * i;
            Vector3 dir = Quaternion.Euler(0f, angle, 0f) * baseDir;

            PatternGuideHelper.Sphere(
                origin + dir * 2f,
                0.35f,
                PatternGuideHelper.Telegraph,
                lifetime: 0.5f);

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
                // 즉발 폴백: 중앙 구체만 판정
                if (i != count / 2) continue;
                float dist = Vector3.Distance(ctx.Transform.position, ctx.Runtime.PlayerTarget.position);
                if (dist > Data.maxRange) continue;
                var player = ctx.Runtime.PlayerTarget.GetComponent<PlayerController>();
                if (player == null) continue;
                int dmg = Mathf.Max(1, (int)(ctx.Config.stat.attackPower * Data.damageMultiplier));
                player.TakeDamage(dmg);
            }
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
