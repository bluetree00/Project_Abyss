using Abyss.Monster;
using UnityEngine;

/// <summary>
/// BT 리프 노드 — OverheadSlash (근접 강타).
/// FullLockState: 실행 중 피격 차단.
///
/// CanExecute: OverheadCooldown ≤ 0
/// 완료 시 직접 ChaseState 로 복귀.
/// </summary>
public class BKOverheadSlashState : FullLockState<BKOverheadSlashPatternSO>
{
    private readonly BossAttackBlackboard _bb;

    private float _timer;
    private bool  _hitDealt;

    private BossWarningIndicator _indicator;

    public BKOverheadSlashState(BKOverheadSlashPatternSO data, BossAttackBlackboard bb) : base(data)
    {
        _bb = bb;
    }

    // ── BT 조건 ──────────────────────────────────────────
    public bool CanExecute(MonsterContext ctx)
    {
        if (_bb.OverheadCooldown > 0f) return false;
        // 최소 평타 시간이 지나야 발동 — 패턴 직후 즉시 재발동 방지
        if (_bb.NormalModeTimer < Data.minBasicAttackDuration) return false;
        if (ctx.Runtime.PlayerTarget == null) return false;
        float dist = UnityEngine.Vector3.Distance(ctx.Transform.position, ctx.Runtime.PlayerTarget.position);
        return dist <= ctx.Stat.attackRange;
    }

    // ── FSM Enter/Update/Exit ────────────────────────────
    public override void Enter(MonsterContext ctx)
    {
        _timer     = Data.overheadDuration;
        _hitDealt  = false;
        _indicator = ctx.Transform.GetComponent<BossWarningIndicator>();

        _bb.AudioPool?.Play(ctx.Transform.position, Data.overheadSfx, 0.5f);

        if (ctx.Agent.isActiveAndEnabled && ctx.Agent.isOnNavMesh) ctx.Agent.ResetPath();
        FacePlayer(ctx);

        // 경고 장판: 히트 타임까지 표시
        _indicator?.ShowCircle(ctx.Transform, Data.overheadRadius, Data.overheadHitTime);

        ctx.Animator?.CrossFade(Data.overheadAnimState, 0.1f, 0, 0f);
    }

    public override void Update(MonsterContext ctx)
    {
        _timer -= Time.deltaTime;

        float elapsed = Data.overheadDuration - _timer;
        if (!_hitDealt && elapsed >= Data.overheadHitTime)
        {
            _hitDealt = true;
            _indicator?.HideCircle(); // 히트 순간 장판 즉시 제거
            DealDamage(ctx);
        }

        if (_timer <= 0f)
            ctx.Monster.ChangeState<ChaseState>();
    }

    public override void Exit(MonsterContext ctx)
    {
        _indicator?.HideCircle(); // 안전 차단
        _bb.OverheadCooldown = Data.overheadCooldown;
    }

    // ── 데미지 ───────────────────────────────────────────
    private void DealDamage(MonsterContext ctx)
    {
        if (ctx.Runtime.PlayerTarget == null) return;

        float dist = Vector3.Distance(ctx.Transform.position, ctx.Runtime.PlayerTarget.position);
        if (dist > Data.overheadRadius) return;

        var player = ctx.Runtime.PlayerTarget.GetComponent<PlayerController>();
        if (player == null) return;

        int dmg = Mathf.RoundToInt(ctx.Config.stat.attackPower
                                   * Data.overheadDamageMul
                                   * ctx.Runtime.AttackMultiplier);
        player.TakeDamage(dmg);

        var rb = ctx.Runtime.PlayerTarget.GetComponent<Rigidbody>();
        if (rb != null)
        {
            Vector3 dir = (ctx.Runtime.PlayerTarget.position - ctx.Transform.position).normalized;
            dir.y = Data.overheadKnockbackY;
            rb.AddForce(dir.normalized * ctx.Config.stat.knockbackForce, ForceMode.Impulse);
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
