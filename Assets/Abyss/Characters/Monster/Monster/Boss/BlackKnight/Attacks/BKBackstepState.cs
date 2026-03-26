using Abyss.Monster;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// BT 리프 노드 — Backstep (후퇴 회피).
/// FullLockState: 실행 중 피격 차단.
///
/// CanExecute: BackstepCooldown ≤ 0
/// 흐름:
///   ① WindUp  (backstepWindUp 초) — 플레이어를 바라보며 후퇴 준비 자세
///   ② Move    (backstepDuration 초) — 플레이어 반대 방향으로 빠르게 이동
///              NavMesh 꺼짐, AttackSpeedMult 가 각성 시 이동 속도에 반영
///   ③ Recovery(0.15 초) — 짧은 정지, 착지 느낌
///   ④ ChaseState 복귀 (breakOverride 로 빠른 연계 패턴 발동)
/// </summary>
public class BKBackstepState : FullLockState<BKBackstepPatternSO>
{
    private readonly BossAttackBlackboard _bb;

    private enum Phase { WindUp, Move, Recovery, Done }

    private Phase   _phase;
    private float   _timer;
    private Vector3 _backstepDir;
    private float   _startY;

    public BKBackstepState(BKBackstepPatternSO data, BossAttackBlackboard bb) : base(data)
    {
        _bb = bb;
    }

    // ── BT 조건 ──────────────────────────────────────────
    public bool CanExecute(MonsterContext ctx)
        => _bb.BackstepCooldown <= 0f && ctx.Runtime.PlayerTarget != null;

    // ── FSM Enter/Update/Exit ────────────────────────────
    public override void Enter(MonsterContext ctx)
    {
        _startY = ctx.Transform.position.y;

        // 플레이어 반대 방향 계산 (후퇴 방향)
        if (ctx.Runtime.PlayerTarget != null)
        {
            Vector3 toPlayer = ctx.Runtime.PlayerTarget.position - ctx.Transform.position;
            toPlayer.y = 0f;
            _backstepDir = toPlayer.sqrMagnitude > 0.001f
                ? -toPlayer.normalized
                : -ctx.Transform.forward;
            // 플레이어를 바라본 채로 후퇴
            ctx.Transform.rotation = Quaternion.LookRotation(-_backstepDir);
        }
        else
        {
            _backstepDir = -ctx.Transform.forward;
        }

        ctx.Agent.ResetPath();
        ctx.Agent.enabled = false;

        _bb.AudioPool?.Play(ctx.Transform.position, Data.backstepSfx, 0.5f);
        ctx.Animator?.CrossFade(Data.backstepAnimState, 0.1f);

        _phase = Phase.WindUp;
        _timer = Data.backstepWindUp;
    }

    public override void Update(MonsterContext ctx)
    {
        float dt = Time.deltaTime;
        _timer -= dt;

        switch (_phase)
        {
            // ── 준비 ──────────────────────────────────────
            case Phase.WindUp:
                if (_timer <= 0f)
                {
                    _phase = Phase.Move;
                    _timer = Data.backstepDuration;
                }
                break;

            // ── 후퇴 이동 ────────────────────────────────
            case Phase.Move:
            {
                float speed = (Data.backstepDistance / Data.backstepDuration) * _bb.AttackSpeedMult;
                Vector3 next = ctx.Transform.position + _backstepDir * speed * dt;
                next.y = _startY;
                ctx.Transform.position = next;

                if (_timer <= 0f)
                {
                    _phase = Phase.Recovery;
                    _timer = 0.15f;
                }
                break;
            }

            // ── 짧은 정지 ────────────────────────────────
            case Phase.Recovery:
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
        _bb.BackstepCooldown = Data.backstepCooldown;

        // NavMesh 복구
        if (ctx.Agent != null && !ctx.Agent.enabled)
        {
            Vector3 endPos = ctx.Transform.position;
            endPos.y = _startY;
            if (NavMesh.SamplePosition(endPos, out NavMeshHit hit, 4f, NavMesh.AllAreas))
                endPos = hit.position;
            ctx.Transform.position = endPos;
            ctx.Agent.enabled      = true;
            ctx.Agent.Warp(endPos);
        }
    }
}
