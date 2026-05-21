using UnityEngine;

namespace RelicFairy.Monster
{
/// <summary>
/// DeathKnight 연속 베기 (ComboStrike) 패턴.
///
/// 흐름  : Attack2 애니메이션 → hitTime1 에 1타 판정 → hitTime2 에 2타 판정 → 복귀
/// 조건  : 플레이어가 range 이내
/// </summary>
[CreateAssetMenu(menuName = "RelicFairy/Boss/DeathKnight/DK_ComboStrikePattern", fileName = "DK_ComboStrikePattern")]
public class DKComboStrikePatternSO : BossPatternSO
{
    [Header("ComboStrike — Range")]
    public float range = 3.0f;
    public float angle = 100f;

    [Header("ComboStrike — Timing")]
    [Tooltip("1타 타격 시점 (초)")]
    public float hitTime1 = 0.45f;
    [Tooltip("2타 타격 시점 (초)")]
    public float hitTime2 = 0.85f;
    [Tooltip("2타 이후 복귀 대기 (초)")]
    public float recoveryTime = 0.5f;

    [Header("ComboStrike — Damage")]
    [Tooltip("1타 attackPower 배율")]
    public float damageMultiplier1 = 0.8f;
    [Tooltip("2타 attackPower 배율")]
    public float damageMultiplier2 = 1.2f;
    public float knockbackMultiplier = 1.2f;

    // ── 런타임 ───────────────────────────────────────────
    private DKComboStrikeState _state;

    public override void Initialize(BossPatternContext ctx) => _state = new DKComboStrikeState(this);
    public override void OnRecycled()                      => _state = new DKComboStrikeState(this);

    public override bool CanExecute(BossPatternContext ctx)
    {
        if (ctx.Ctx.Runtime.PlayerTarget == null) return false;
        return ctx.Ctx.Runtime.DistToPlayer <= range;
    }

    public override SpecialStateBase GetRuntimeState() => _state;
}

// ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
// DKComboStrikeState
// ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

public class DKComboStrikeState : FullLockState<DKComboStrikePatternSO>
{
    private const string AnimName = "Attack2";

    private float _timer;
    private bool  _hit1;
    private bool  _hit2;

    public DKComboStrikeState(DKComboStrikePatternSO data) : base(data) { }

    public override void Enter(MonsterContext ctx)
    {
        _timer = 0f;
        _hit1  = false;
        _hit2  = false;

        StopAgent(ctx);
        FacePlayer(ctx);
        PlayAnim(ctx, AnimName);
    }

    public override void Update(MonsterContext ctx)
    {
        _timer += Time.deltaTime * AnimSpeed(ctx);

        if (!_hit1 && _timer >= Data.hitTime1)
        {
            _hit1 = true;
            DealDamage(ctx, Data.damageMultiplier1);
        }

        if (!_hit2 && _timer >= Data.hitTime2)
        {
            _hit2 = true;
            DealDamage(ctx, Data.damageMultiplier2);
        }

        if (_timer >= Data.hitTime2 + Data.recoveryTime)
            ctx.Monster.ChangeState<ChaseState>();
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

    private void DealDamage(MonsterContext ctx, float mult)
    {
        if (ctx.Config?.stat == null || ctx.Runtime.PlayerTarget == null) return;

        Vector3 toPlayer = ctx.Runtime.PlayerTarget.position - ctx.Transform.position;
        toPlayer.y = 0f;

        if (toPlayer.magnitude > Data.range) return;
        if (Vector3.Angle(ctx.Transform.forward, toPlayer) > Data.angle * 0.5f) return;

        var player = ctx.Runtime.PlayerTarget.GetComponent<PlayerController>();
        if (player == null) return;

        int dmg = Mathf.Max(1, (int)(ctx.Config.stat.attackPower * mult));
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
            Debug.LogWarning($"[DKComboStrike] Animator state not found: '{stateName}'", ctx.Monster);
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
