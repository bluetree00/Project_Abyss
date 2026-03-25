using Abyss.Monster;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// BT 리프 노드 — ChargeAttack (돌진 공격).
/// FullLockState: 실행 중 피격 차단.
///
/// CanExecute: chargeMinDist ≤ dist ≤ chargeMaxDist AND ChargeCooldown ≤ 0
/// 완료 시 직접 ChaseState 로 복귀.
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
    public bool CanExecute(MonsterContext ctx)
    {
        if (_bb.ChargeCooldown > 0f) return false;
        if (ctx.Runtime.PlayerTarget == null) return false;
        float dist = Vector3.Distance(ctx.Transform.position, ctx.Runtime.PlayerTarget.position);
        return dist >= Data.chargeMinDist && dist <= Data.chargeMaxDist;
    }

    // ── FSM Enter/Update/Exit ────────────────────────────
    public override void Enter(MonsterContext ctx)
    {
        _timer            = Data.chargeDuration;
        _chargeStartTimer = Data.chargeDuration * Data.chargeWindUpRatio;
        _charging         = false;
        _damageDealt      = false;
        _indicator        = ctx.Transform.GetComponent<BossWarningIndicator>();

        _bb.AudioPool?.Play(ctx.Transform.position, Data.chargeSfx, 0.5f);

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

        // 경고 장판: 바람업 동안 돌진 방향 화살표 표시
        float windUpTime   = Data.chargeDuration * Data.chargeWindUpRatio;
        float chargeLength = Data.chargeSpeed * Data.chargeDuration * (1f - Data.chargeWindUpRatio);
        _indicator?.ShowCharge(ctx.Transform.position, _chargeDir,
                               chargeLength, Data.chargeRadius * 2f, windUpTime);

        ctx.Animator?.CrossFade(Data.chargeAnimState, 0.1f);
    }

    public override void Update(MonsterContext ctx)
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
            ctx.Monster.ChangeState<ChaseState>();
    }

    public override void Exit(MonsterContext ctx)
    {
        _indicator?.HideCharge();
        _bb.ChargeCooldown = Data.chargeCooldown;
        if (ctx.Agent != null)
        {
            // 돌진 중 Y를 _startY로 고정했으므로 그 위치 기준으로 NavMesh 스냅
            Vector3 endPos = ctx.Transform.position;
            endPos.y = _startY; // 혹시 남은 오차 제거
            if (NavMesh.SamplePosition(endPos, out NavMeshHit hit, 4f, NavMesh.AllAreas))
                endPos = hit.position;

            // Transform 먼저 강제 이동 → Agent.enabled 시 OnEnable 이 올바른 위치 인식
            ctx.Transform.position = endPos;
            ctx.Agent.enabled      = true;
            ctx.Agent.Warp(endPos);
        }
    }

    // ── 데미지 ───────────────────────────────────────────
    private void DealChargeDamage(MonsterContext ctx)
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
            Vector3 dir = _chargeDir + Vector3.up * Data.chargeKnockbackY;
            rb.AddForce(dir.normalized * ctx.Config.stat.knockbackForce * Data.chargeKnockbackMul, ForceMode.Impulse);
        }
    }
}
