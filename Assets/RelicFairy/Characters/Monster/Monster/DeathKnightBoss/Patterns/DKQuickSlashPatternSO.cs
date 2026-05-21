using UnityEngine;

namespace RelicFairy.Monster
{
/// <summary>
/// DeathKnight 빠른 베기 (QuickSlash) 패턴.
///
/// 흐름  : 플레이어를 향해 즉시 회전 → Attack1 애니메이션
///         → hitTime 에 부채꼴 범위 판정 → 복귀
/// 조건  : 플레이어가 range 이내
/// </summary>
[CreateAssetMenu(menuName = "RelicFairy/Boss/DeathKnight/DK_QuickSlashPattern", fileName = "DK_QuickSlashPattern")]
public class DKQuickSlashPatternSO : BossPatternSO
{
    [Header("QuickSlash — Range")]
    [Tooltip("공격 사정거리 (m)")]
    public float range = 2.5f;

    [Tooltip("공격 부채꼴 각도 (°)")]
    public float angle = 120f;

    [Header("QuickSlash — Timing")]
    [Tooltip("애니메이션 시작 후 실제 타격 판정까지 대기 시간 (초)")]
    public float hitTime = 0.5f;

    [Tooltip("타격 판정 후 복귀까지 대기 시간 (초)")]
    public float recoveryTime = 0.4f;

    [Header("QuickSlash — Damage")]
    [Tooltip("attackPower 배율")]
    public float damageMultiplier = 1.0f;

    [Tooltip("넉백 배율")]
    public float knockbackMultiplier = 1.0f;

    // ── 런타임 ───────────────────────────────────────────
    private DKQuickSlashState _state;

    public override void Initialize(BossPatternContext ctx) => _state = new DKQuickSlashState(this);
    public override void OnRecycled()                      => _state = new DKQuickSlashState(this);

    public override bool CanExecute(BossPatternContext ctx)
    {
        if (ctx.Ctx.Runtime.PlayerTarget == null) return false;
        return ctx.Ctx.Runtime.DistToPlayer <= range;
    }

    public override SpecialStateBase GetRuntimeState() => _state;
}

// ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
// DKQuickSlashState
// ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

public class DKQuickSlashState : FullLockState<DKQuickSlashPatternSO>
{
    private const string AnimName = "Attack1";

    private enum Phase { Swing, Recovery }
    private Phase _phase;
    private float _timer;
    private bool  _hasDamaged;

    public DKQuickSlashState(DKQuickSlashPatternSO data) : base(data) { }

    public override void Enter(MonsterContext ctx)
    {
        _phase      = Phase.Swing;
        _timer      = 0f;
        _hasDamaged = false;

        StopAgent(ctx);
        FacePlayer(ctx);
        PlayAnim(ctx, AnimName);
    }

    public override void Update(MonsterContext ctx)
    {
        _timer += Time.deltaTime * AnimSpeed(ctx);

        switch (_phase)
        {
            case Phase.Swing:
                if (!_hasDamaged && _timer >= Data.hitTime)
                {
                    _hasDamaged = true;
                    DealDamage(ctx);
                }
                if (_timer >= Data.hitTime + Data.recoveryTime)
                {
                    _phase = Phase.Recovery;
                    ctx.Monster.ChangeState<ChaseState>();
                }
                break;
        }
    }

    public override void Exit(MonsterContext ctx) => RestoreAgent(ctx);

    private static void StopAgent(MonsterContext ctx)
    {
        if (ctx.Agent != null && ctx.Agent.isOnNavMesh)
        {
            ctx.Agent.isStopped = true;
            ctx.Agent.ResetPath();
        }
    }

    private static void RestoreAgent(MonsterContext ctx)
    {
        if (ctx.Agent != null && ctx.Agent.isOnNavMesh)
            ctx.Agent.isStopped = false;
    }

    private static void FacePlayer(MonsterContext ctx)
    {
        if (ctx.Runtime.PlayerTarget == null) return;
        Vector3 dir = ctx.Runtime.PlayerTarget.position - ctx.Transform.position;
        dir.y = 0f;
        if (dir.sqrMagnitude > 0.001f)
            ctx.Transform.rotation = Quaternion.LookRotation(dir);
    }

    private void DealDamage(MonsterContext ctx)
    {
        if (ctx.Config?.stat == null || ctx.Runtime.PlayerTarget == null) return;

        Vector3 toPlayer = ctx.Runtime.PlayerTarget.position - ctx.Transform.position;
        toPlayer.y = 0f;

        if (toPlayer.magnitude > Data.range) return;

        float halfAngle = Data.angle * 0.5f;
        if (Vector3.Angle(ctx.Transform.forward, toPlayer) > halfAngle) return;

        var player = ctx.Runtime.PlayerTarget.GetComponent<PlayerController>();
        if (player == null) return;

        int dmg = Mathf.Max(1, (int)(ctx.Config.stat.attackPower * Data.damageMultiplier));
        player.TakeDamage(dmg);

        Vector3 knockDir = toPlayer.sqrMagnitude > 0.001f ? toPlayer.normalized : ctx.Transform.forward;
        knockDir.y = 0.2f;
        player.ApplyKnockback(knockDir * ctx.Config.stat.knockbackForce * Data.knockbackMultiplier);
    }

    private static void PlayAnim(MonsterContext ctx, string stateName)
    {
        if (ctx.Animator == null) return;
        if (!ctx.Animator.HasState(0, Animator.StringToHash(stateName)))
        {
            Debug.LogWarning($"[DKQuickSlash] Animator state not found: '{stateName}'", ctx.Monster);
            return;
        }
        ctx.Animator.speed = AnimSpeed(ctx);
        ctx.Animator.CrossFade(stateName, 0.05f, 0, 0f);
    }

    private static float AnimSpeed(MonsterContext ctx)
    {
        var dk = ctx.Monster as DeathKnightBossMonster;
        return dk?.DKBlackboard.AnimSpeedMult ?? 1f;
    }
}
}
