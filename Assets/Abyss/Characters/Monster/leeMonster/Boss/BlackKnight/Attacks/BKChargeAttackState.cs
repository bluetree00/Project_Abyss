using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// BT 리프 노드 — ChargeAttack (돌진 공격).
/// FullLockState: 실행 중 피격 차단.
///
/// CanExecute: chargeMinDist ≤ dist ≤ chargeMaxDist AND ChargeCooldown ≤ 0
/// 완료 시 직접 LeeChaseState 로 복귀.
/// </summary>
public class BKChargeAttackState : FullLockState<BlackKnightPatternData>
{
    private readonly BossAttackBlackboard _bb;

    private Vector3 _chargeDir;
    private float   _timer;
    private float   _chargeStartTimer;
    private bool    _charging;
    private bool    _damageDealt;
    private float   _startY; // 돌진 시작 Y 고정값

    private BossWarningIndicator _indicator;

    public BKChargeAttackState(BlackKnightPatternData data, BossAttackBlackboard bb) : base(data)
    {
        _bb = bb;
    }

    // ── BT 조건 ──────────────────────────────────────────
    public bool CanExecute(LeeMonsterContext ctx)
    {
        if (_bb.ChargeCooldown > 0f) return false;
        if (ctx.Runtime.PlayerTarget == null) return false;
        float dist = Vector3.Distance(ctx.Transform.position, ctx.Runtime.PlayerTarget.position);
        return dist >= Data.chargeMinDist && dist <= Data.chargeMaxDist;
    }

    // ── FSM Enter/Update/Exit ────────────────────────────
    public override void Enter(LeeMonsterContext ctx)
    {
        _timer            = Data.chargeDuration;
        _chargeStartTimer = Data.chargeDuration * 0.3f;
        _charging         = false;
        _damageDealt      = false;
        _indicator        = ctx.Transform.GetComponent<BossWarningIndicator>();

        _startY = ctx.Transform.position.y; // 돌진 중 Y 고정 기준

        ctx.Agent.ResetPath();
        ctx.Agent.enabled = false; // 돌진 중 NavMesh 제어 해제

        if (ctx.Runtime.PlayerTarget != null)
        {
            Vector3 toTarget = ctx.Runtime.PlayerTarget.position - ctx.Transform.position;
            toTarget.y   = 0f;
            _chargeDir   = toTarget.sqrMagnitude > 0.001f ? toTarget.normalized : ctx.Transform.forward;
            ctx.Transform.rotation = Quaternion.LookRotation(_chargeDir);
        }

        // 경고 장판: 바람업(30%) 동안 돌진 방향 화살표 표시
        float windUpTime   = Data.chargeDuration * 0.3f;
        float chargeLength = Data.chargeSpeed * Data.chargeDuration * 0.7f;
        _indicator?.ShowCharge(ctx.Transform.position, _chargeDir,
                               chargeLength, Data.chargeRadius * 2f, windUpTime);

        ctx.Animator?.CrossFade(Data.chargeAnimState, 0.1f);
    }

    public override void Update(LeeMonsterContext ctx)
    {
        _timer            -= Time.deltaTime;
        _chargeStartTimer -= Time.deltaTime;

        if (!_charging && _chargeStartTimer <= 0f)
        {
            _charging = true;
            _indicator?.HideCharge(); // 돌진 시작 시 장판 즉시 제거
        }

        if (_charging)
        {
            Vector3 next = ctx.Transform.position + _chargeDir * Data.chargeSpeed * Time.deltaTime;
            next.y = _startY; // Y 고정 — 지면 위로 뜨는 현상 방지
            ctx.Transform.position = next;

            if (!_damageDealt && ctx.Runtime.PlayerTarget != null)
            {
                float dist = Vector3.Distance(ctx.Transform.position, ctx.Runtime.PlayerTarget.position);
                if (dist <= Data.chargeRadius)
                {
                    _damageDealt = true;
                    DealChargeDamage(ctx);
                }
            }
        }

        if (_timer <= 0f)
            ctx.Monster.ChangeState<LeeChaseState>();
    }

    public override void Exit(LeeMonsterContext ctx)
    {
        _indicator?.HideCharge(); // 안전 차단
        _bb.ChargeCooldown = Data.chargeCooldown;
        if (ctx.Agent != null)
        {
            // enabled = true 시 NavMeshAgent OnEnable 이 transform.position 을 내부 위치로
            // 덮어쓸 수 있으므로, 먼저 돌진 종료 위치를 저장 후 NavMesh 표면에 스냅한다.
            Vector3 endPos = ctx.Transform.position;
            if (NavMesh.SamplePosition(endPos, out NavMeshHit hit, 2f, NavMesh.AllAreas))
                endPos = hit.position;

            ctx.Agent.enabled = true;
            ctx.Agent.Warp(endPos); // 저장된 위치로 Warp — 에이전트 스냅 위치 무시
        }
    }

    // ── 데미지 ───────────────────────────────────────────
    private void DealChargeDamage(LeeMonsterContext ctx)
    {
        if (ctx.Runtime.PlayerTarget == null) return;

        var player = ctx.Runtime.PlayerTarget.GetComponent<PlayerController>();
        if (player == null) return;

        int dmg = Mathf.RoundToInt(ctx.Config.stat.attackPower
                                   * Data.chargeDamageMul
                                   * ctx.Runtime.AttackMultiplier);
        player.TakeDamage(dmg);

        var rb = ctx.Runtime.PlayerTarget.GetComponent<Rigidbody>();
        if (rb != null)
        {
            Vector3 dir = _chargeDir + Vector3.up * 0.5f;
            rb.AddForce(dir.normalized * ctx.Config.stat.knockbackForce * 2.5f, ForceMode.Impulse);
        }
    }
}
