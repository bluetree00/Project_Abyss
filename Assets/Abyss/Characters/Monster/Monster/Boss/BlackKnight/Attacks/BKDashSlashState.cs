using Abyss.Monster;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// BT 리프 노드 — DashSlash (돌진 베기).
/// FullLockState: 실행 중 피격 차단.
///
/// CanExecute: DashSlashCooldown ≤ 0, 거리 범위 내
/// 흐름:
///   ① WindUp  (dashWindUp 초) — 경고 인디케이터 표시, 자세 잡기
///   ② Dash    (dashDuration 초) — 플레이어 방향으로 고속 이동 (AttackSpeedMult 적용)
///              진입 후 위치 고정(Lock) → 보스가 정해진 방향으로만 돌진
///   ③ Slash   (slashDuration 초) — 도착 지점에서 원형 AoE 피해
///   ④ ChaseState 복귀
///
/// ChargeAttack 과의 차이:
///   • 더 짧고 빠른 이동 (고속 돌진)
///   • 피해는 도착 시 한 번의 원형 판정 (돌진 중 히트박스 없음)
///   • 거리 범위가 넓어 원거리에서도 사용 가능
///   • Backstep 후 연계 패턴으로 주로 사용
/// </summary>
public class BKDashSlashState : FullLockState<BKDashSlashPatternSO>
{
    private readonly BossAttackBlackboard _bb;

    private enum Phase { WindUp, Dash, Slash, Done }

    private Phase   _phase;
    private float   _timer;
    private Vector3 _dashDir;
    private Vector3 _targetPos;  // 돌진 목표 위치 (Enter 시 고정)
    private float   _startY;
    private bool    _hitDealt;

    private BossWarningIndicator _indicator;

    public BKDashSlashState(BKDashSlashPatternSO data, BossAttackBlackboard bb) : base(data)
    {
        _bb = bb;
    }

    // ── BT 조건 ──────────────────────────────────────────
    public bool CanExecute(MonsterContext ctx)
    {
        if (_bb.DashSlashCooldown > 0f) return false;
        if (ctx.Runtime.PlayerTarget == null) return false;
        float dist = Vector3.Distance(ctx.Transform.position, ctx.Runtime.PlayerTarget.position);
        return dist >= Data.dashMinDist && dist <= Data.dashMaxDist;
    }

    // ── FSM Enter/Update/Exit ────────────────────────────
    public override void Enter(MonsterContext ctx)
    {
        _indicator = ctx.Transform.GetComponent<BossWarningIndicator>();
        _startY    = ctx.Transform.position.y;
        _hitDealt  = false;

        // 목표 위치 및 방향 고정 (Enter 시점의 플레이어 위치)
        if (ctx.Runtime.PlayerTarget != null)
        {
            _targetPos = ctx.Runtime.PlayerTarget.position;
            _targetPos.y = _startY;
        }
        else
        {
            _targetPos = ctx.Transform.position + ctx.Transform.forward * Data.dashMaxDist;
        }

        Vector3 toTarget = _targetPos - ctx.Transform.position;
        toTarget.y = 0f;
        _dashDir = toTarget.sqrMagnitude > 0.001f ? toTarget.normalized : ctx.Transform.forward;
        ctx.Transform.rotation = Quaternion.LookRotation(_dashDir);

        if (ctx.Agent.isActiveAndEnabled && ctx.Agent.isOnNavMesh) ctx.Agent.ResetPath();
        ctx.Agent.enabled = false;

        _bb.AudioPool?.Play(ctx.Transform.position, Data.dashSfx, 0.5f);
        ctx.Animator?.CrossFade(Data.dashAnimState, 0.05f, 0, 0f);

        // 경고 인디케이터 — 돌진 예정 경로 표시
        float dashLength = Data.dashSpeed * Data.dashDuration * _bb.AttackSpeedMult;
        _indicator?.ShowCharge(ctx.Transform.position, _dashDir,
                               dashLength, Data.slashRadius * 2f, Data.dashWindUp);

        _phase = Phase.WindUp;
        _timer = Data.dashWindUp;
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
                    _indicator?.HideCharge();
                    _bb.AudioPool?.Play(ctx.Transform.position, Data.dashSfx, 0.3f);
                    _phase = Phase.Dash;
                    _timer = Data.dashDuration;
                }
                break;

            // ── 고속 돌진 ────────────────────────────────
            case Phase.Dash:
            {
                float speed = Data.dashSpeed * _bb.AttackSpeedMult;
                Vector3 next = ctx.Transform.position + _dashDir * speed * dt;
                next.y = _startY;
                ctx.Transform.position = next;

                if (_timer <= 0f)
                {
                    // 도착 — NavMesh 복구 후 베기 판정
                    SnapToNavMesh(ctx);

                    _bb.AudioPool?.Play(ctx.Transform.position, Data.slashSfx, 0.6f);

                    _phase = Phase.Slash;
                    _timer = Data.slashDuration;
                }
                break;
            }

            // ── 베기 판정 ────────────────────────────────
            case Phase.Slash:
                // 첫 프레임에 피해 처리
                if (!_hitDealt)
                {
                    _hitDealt = true;
                    DealSlashDamage(ctx);
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
        _indicator?.HideCharge();
        _bb.DashSlashCooldown = Data.dashCooldown;

        // 비정상 종료 대비 NavMesh 복구
        if (ctx.Agent != null && !ctx.Agent.enabled)
            SnapToNavMesh(ctx);
    }

    // ── 헬퍼 ─────────────────────────────────────────────

    private void SnapToNavMesh(MonsterContext ctx)
    {
        if (ctx.Agent == null) return;
        Vector3 pos = ctx.Transform.position;
        pos.y = _startY;
        if (NavMesh.SamplePosition(pos, out NavMeshHit hit, 4f, NavMesh.AllAreas))
            pos = hit.position;
        ctx.Transform.position = pos;
        ctx.Agent.enabled      = true;
        ctx.Agent.Warp(pos);
        ctx.Agent.ResetPath();
    }

    private void DealSlashDamage(MonsterContext ctx)
    {
        var cols = Physics.OverlapSphere(ctx.Transform.position, Data.slashRadius);
        foreach (var col in cols)
        {
            var player = col.GetComponent<PlayerController>()
                      ?? col.GetComponentInParent<PlayerController>();
            if (player == null) continue;

            int dmg = Mathf.RoundToInt(ctx.Config.stat.attackPower
                                       * Data.slashDamageMul
                                       * ctx.Runtime.AttackMultiplier);
            player.TakeDamage(dmg);

            var rb = col.GetComponent<Rigidbody>() ?? col.GetComponentInParent<Rigidbody>();
            if (rb != null)
            {
                Vector3 dir = (_dashDir + Vector3.up * Data.knockbackY).normalized;
                rb.AddForce(dir * ctx.Config.stat.knockbackForce * Data.knockbackMul,
                            ForceMode.Impulse);
            }
            break; // 플레이어는 1명
        }
    }
}
