using UnityEngine;

namespace RelicFairy.Monster
{
/// <summary>
/// 리치 낫 휩쓸기 (Scythe Sweep) 패턴 — Phase 2 일반 공격.
///
/// 흐름: DashClose 힌트(접근) → 근접 도달 또는 대기(approachDuration) →
///       전방 호형 범위(sweepRadius, sweepAngle) 데미지 → 복귀(recoveryDuration)
/// </summary>
[CreateAssetMenu(menuName = "RelicFairy/Boss/Lich/Lich_ScytheSweepPattern", fileName = "Lich_ScytheSweepPattern")]
public class LichScytheSweepPatternSO : BossPatternSO
{
    [Header("Scythe Sweep — Range")]
    [Tooltip("패턴 발동 최소 거리: 이 이상 멀 때만 실행 (근접 전용)")]
    public float minTriggerRange = 3f;
    [Tooltip("패턴 발동 최대 거리")]
    public float maxTriggerRange = 20f;

    [Header("Scythe Sweep — Timing")]
    [Tooltip("DashClose 이동 대기 시간 (초)")]
    public float approachDuration = 0.8f;
    [Tooltip("근접 후 스윕 선딜 (초)")]
    public float sweepDelay = 0.2f;
    [Tooltip("복귀 대기 시간 (초)")]
    public float recoveryDuration = 0.5f;

    [Header("Scythe Sweep — Hit")]
    [Tooltip("휩쓸기 판정 반경 (m)")]
    public float sweepRadius = 4f;
    [Tooltip("전방 판정 호 반각 (도). 90 = 전방 180도 범위.")]
    public float sweepHalfAngle = 90f;

    [Header("Scythe Sweep — Damage")]
    [Tooltip("기본 attackPower에 곱할 배율")]
    public float damageMultiplier = 1.6f;
    [Tooltip("넉백 힘 배율")]
    public float knockbackMultiplier = 2.0f;

    [Header("Scythe Sweep — Cooldown")]
    [Tooltip("패턴 완료 후 재사용 대기 시간 (초)")]
    public float patternCooldown = 7f;

    // ── 런타임 ───────────────────────────────────────────
    private LichScytheSweepState _state;

    public override void Initialize(BossPatternContext ctx) => _state = new LichScytheSweepState(this);
    public override void OnRecycled()                       => _state = new LichScytheSweepState(this);

    public override bool CanExecute(BossPatternContext ctx)
    {
        if (ctx.Ctx.Runtime.PlayerTarget == null) return false;
        var lichBB = (ctx.Boss as LichMonster)?.LichBB;
        if (lichBB == null || !lichBB.IsPhase2) return false;
        if (lichBB.ScytheSweepCooldown > 0f) return false;
        float dist = Vector3.Distance(ctx.Ctx.Transform.position, ctx.Ctx.Runtime.PlayerTarget.position);
        return dist >= minTriggerRange && dist <= maxTriggerRange;
    }

    public override SpecialStateBase GetRuntimeState() => _state;
}

// ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
// LichScytheSweepState — UnInterruptible (이동 자유)
// ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

public class LichScytheSweepState : UnInterruptibleState<LichScytheSweepPatternSO>
{
    private enum Phase { Approach, SweepDelay, Sweep, Recovery }

    private Phase      _phase;
    private float      _timer;
    private bool       _hasDealt;
    private GameObject _sweepGuide;

    public LichScytheSweepState(LichScytheSweepPatternSO data) : base(data) { }

    public override void Enter(MonsterContext ctx)
    {
        _phase    = Phase.Approach;
        _timer    = 0f;
        _hasDealt = false;

        ctx.Animator?.CrossFade("ScytheSweep", 0.1f);

        var mc = (ctx.Monster as LichMonster)?.MovementController;
        mc?.RequestMovementState(LichMovementState.DashClose);
        mc?.SetLocked(true);

        // Approach 시작 시 Telegraph disc 미리 표시 — 돌진 중 따라오며 Active로 전환
        _sweepGuide = PatternGuideHelper.Disc(
            ctx.Transform.position,
            Data.sweepRadius,
            PatternGuideHelper.Telegraph);
    }

    public override void Update(MonsterContext ctx)
    {
        _timer += Time.deltaTime;

        switch (_phase)
        {
            case Phase.Approach:
                if (_sweepGuide != null)
                    _sweepGuide.transform.position = ctx.Transform.position;
                if (_timer >= Data.approachDuration)
                {
                    PatternGuideHelper.SetColor(_sweepGuide, PatternGuideHelper.Active);
                    (ctx.Monster as LichMonster)?.MovementController.RequestMovementState(LichMovementState.IdleHover);
                    _phase = Phase.SweepDelay;
                    _timer = 0f;
                }
                break;

            case Phase.SweepDelay:
                // 가이드 위치 갱신
                if (_sweepGuide != null)
                    _sweepGuide.transform.position = ctx.Transform.position;
                if (_timer >= Data.sweepDelay)
                {
                    _phase = Phase.Sweep;
                    _timer = 0f;
                }
                break;

            case Phase.Sweep:
                if (!_hasDealt)
                {
                    _hasDealt = true;
                    DealDamage(ctx);
                    PatternGuideHelper.SafeDestroy(ref _sweepGuide);
                }
                _phase = Phase.Recovery;
                _timer = 0f;
                break;

            case Phase.Recovery:
                if (_timer >= Data.recoveryDuration)
                    ctx.Monster.ChangeState<ChaseState>();
                break;
        }
    }

    public override void Exit(MonsterContext ctx)
    {
        PatternGuideHelper.SafeDestroy(ref _sweepGuide);
        (ctx.Monster as LichMonster)?.MovementController?.SetLocked(false);

        var lich = ctx.Monster as LichMonster;
        if (lich?.LichBB != null)
            lich.LichBB.ScytheSweepCooldown = Data.patternCooldown;
    }

    private void DealDamage(MonsterContext ctx)
    {
        if (ctx.Config?.stat == null || ctx.Runtime.PlayerTarget == null) return;

        Vector3 toPlayer = ctx.Runtime.PlayerTarget.position - ctx.Transform.position;
        float   dist     = toPlayer.magnitude;
        if (dist > Data.sweepRadius) return;

        // 호형 판정
        Vector3 flat = toPlayer;
        flat.y = 0f;
        if (flat.sqrMagnitude > 0.001f &&
            Vector3.Angle(ctx.Transform.forward, flat) > Data.sweepHalfAngle)
            return;

        var player = ctx.Runtime.PlayerTarget.GetComponent<PlayerController>();
        if (player == null) return;

        int dmg = Mathf.Max(1, (int)(ctx.Config.stat.attackPower * Data.damageMultiplier));
        player.TakeDamage(dmg);

        Vector3 dir = toPlayer.normalized;
        dir.y = 0.3f;
        if (dir.sqrMagnitude > 0.001f) dir.Normalize();
        player.ApplyKnockback(dir * ctx.Config.stat.knockbackForce * Data.knockbackMultiplier);
    }
}
}
