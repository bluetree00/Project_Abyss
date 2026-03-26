using Abyss.Monster;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// BT 리프 노드 — Sidestep (측면 이동 플랭크).
/// FullLockState: 실행 중 피격 차단.
///
/// CanExecute: BackstepCooldown ≤ 0, 근접 사거리 내 (Backstep 쿨다운 공유)
/// 흐름:
///   ① Sidestep (sidestepDuration 초) — 플레이어 기준 수직 방향으로 빠르게 이동
///   ② Face     (0.1 초)             — 플레이어 쪽으로 회전 (측면 위치에서 바라봄)
///   ③ ChaseState 복귀 → breakOverride 로 즉시 후속 패턴 발동
///
/// 소울류 "공간 지능" 구현:
///   • 플레이어 정면이 아닌 측면에서 접근해 공격 연계
///   • Backstep과 쿨다운 공유 → 두 회피 기동이 겹치지 않음
///   • breakOverride=0.2 → 이동 직후 DashSlash/Pressure 로 플랭크 공격
///   • Phase 2 (HasEnraged): 기본 보다 20% 빠르게 측면 이동
/// </summary>
public class BKSidestepState : FullLockState<BKSidestepPatternSO>
{
    private readonly BossAttackBlackboard _bb;

    private enum Phase { Sidestep, Face, Done }

    private Phase   _phase;
    private float   _timer;
    private Vector3 _stepDir;
    private float   _startY;

    public BKSidestepState(BKSidestepPatternSO data, BossAttackBlackboard bb) : base(data)
    {
        _bb = bb;
    }

    // ── BT 조건 ──────────────────────────────────────────
    // Backstep 쿨다운을 공유: 두 기동 패턴이 너무 자주 겹치지 않도록
    public bool CanExecute(MonsterContext ctx)
    {
        if (_bb.BackstepCooldown > 0f) return false;
        if (ctx.Runtime.PlayerTarget == null) return false;
        float dist = Vector3.Distance(ctx.Transform.position, ctx.Runtime.PlayerTarget.position);
        return dist <= Data.sidestepRange;
    }

    // ── FSM Enter/Update/Exit ────────────────────────────
    public override void Enter(MonsterContext ctx)
    {
        _startY = ctx.Transform.position.y;

        // 플레이어 방향에서 수직으로 이동 (좌/우 랜덤)
        Vector3 toPlayer = ctx.Runtime.PlayerTarget != null
            ? (ctx.Runtime.PlayerTarget.position - ctx.Transform.position)
            : ctx.Transform.forward;
        toPlayer.y = 0f;
        toPlayer.Normalize();

        float side = Random.value > 0.5f ? 1f : -1f;
        _stepDir = new Vector3(-toPlayer.z * side, 0f, toPlayer.x * side);

        ctx.Agent.ResetPath();
        ctx.Agent.enabled = false;

        _bb.AudioPool?.Play(ctx.Transform.position, Data.sidestepSfx, 0.4f);
        ctx.Animator?.CrossFade(Data.sidestepAnimState, 0.05f);

        _phase = Phase.Sidestep;
        _timer = Data.sidestepDuration;
    }

    public override void Update(MonsterContext ctx)
    {
        float dt = Time.deltaTime;
        _timer -= dt;

        switch (_phase)
        {
            // ── 측면 이동 ────────────────────────────────
            case Phase.Sidestep:
            {
                float speedMult = _bb.HasEnraged ? _bb.AttackSpeedMult * 1.2f : 1f;
                float speed = (Data.sidestepDistance / Data.sidestepDuration) * speedMult;
                Vector3 next = ctx.Transform.position + _stepDir * speed * dt;
                next.y = _startY;
                ctx.Transform.position = next;

                if (_timer <= 0f)
                {
                    _phase = Phase.Face;
                    _timer = 0.08f;
                }
                break;
            }

            // ── 플레이어 방향으로 회전 ────────────────────
            case Phase.Face:
                if (ctx.Runtime.PlayerTarget != null)
                {
                    Vector3 dir = ctx.Runtime.PlayerTarget.position - ctx.Transform.position;
                    dir.y = 0f;
                    if (dir.sqrMagnitude > 0.001f)
                        ctx.Transform.rotation = Quaternion.LookRotation(dir);
                }
                if (_timer <= 0f)
                    _phase = Phase.Done;
                break;

            case Phase.Done:
                ctx.Monster.ChangeState<ChaseState>();
                break;
        }
    }

    public override void Exit(MonsterContext ctx)
    {
        // Backstep 쿨다운 공유 (별도 SidestepCooldown 없음)
        _bb.BackstepCooldown = Data.sidestepCooldown;

        // NavMesh 복구
        if (ctx.Agent != null && !ctx.Agent.enabled)
        {
            Vector3 pos = ctx.Transform.position;
            pos.y = _startY;
            if (NavMesh.SamplePosition(pos, out NavMeshHit hit, 4f, NavMesh.AllAreas))
                pos = hit.position;
            ctx.Transform.position = pos;
            ctx.Agent.enabled      = true;
            ctx.Agent.Warp(pos);
        }
    }
}
