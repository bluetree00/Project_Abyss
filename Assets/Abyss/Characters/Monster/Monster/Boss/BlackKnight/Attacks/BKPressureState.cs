using Abyss.Monster;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// BT 리프 노드 — Pressure (압박 연타 콤보).
/// FullLockState: 실행 중 피격 차단.
///
/// CanExecute: PressureCooldown ≤ 0, 근접 사거리 내
/// 흐름:
///   ① WindUp  (windUpDuration 초) — 자세 잡기, 플레이어 방향 고정
///   ② Hit1    (hit1Duration 초)   — 빠른 1타, hitTime1 에서 AoE 판정
///   ③ Step    (0.1 초)            — 한 발 앞으로 전진 (공격적 포지셔닝)
///   ④ Hit2    (hit2Duration 초)   — 묵직한 2타, hitTime2 에서 AoE 판정
///   ⑤ [각성 시만] StepFwd2 → Hit3 — 회전 강타, 더 넓은 범위
///   ⑥ ChaseState 복귀
///
/// 소울류 보스 "압박" 느낌:
///   • 연타 사이 간격이 매우 짧아 회피하기 어렵다
///   • Phase 2 (HasEnraged) 에서 3타로 확장, 범위도 넓어짐
///   • 각 타격 직전 경고 원 표시 (매우 짧게 — 반응하기 빡빡한 느낌)
/// </summary>
public class BKPressureState : FullLockState<BKPressurePatternSO>
{
    private readonly BossAttackBlackboard _bb;

    private enum Phase { WindUp, Hit1, Step, Hit2, StepFwd2, Hit3, Done }

    private Phase _phase;
    private float _timer;
    private bool  _hit1Dealt;
    private bool  _hit2Dealt;
    private bool  _hit3Dealt;
    private float _startY;

    private BossWarningIndicator _indicator;

    public BKPressureState(BKPressurePatternSO data, BossAttackBlackboard bb) : base(data)
    {
        _bb = bb;
    }

    // ── BT 조건 ──────────────────────────────────────────
    public bool CanExecute(MonsterContext ctx)
    {
        if (_bb.PressureCooldown > 0f) return false;
        if (ctx.Runtime.PlayerTarget == null) return false;
        float dist = Vector3.Distance(ctx.Transform.position, ctx.Runtime.PlayerTarget.position);
        return dist <= Data.pressureRange;
    }

    // ── FSM Enter/Update/Exit ────────────────────────────
    public override void Enter(MonsterContext ctx)
    {
        _indicator = ctx.Transform.GetComponent<BossWarningIndicator>();
        _startY    = ctx.Transform.position.y;
        _hit1Dealt = _hit2Dealt = _hit3Dealt = false;

        ctx.Agent.ResetPath();
        FacePlayer(ctx);

        _bb.AudioPool?.Play(ctx.Transform.position, Data.pressureWindUpSfx, 0.5f);
        ctx.Animator?.CrossFade(Data.hit1AnimState, 0.05f);

        _phase = Phase.WindUp;
        _timer = Data.windUpDuration;
    }

    public override void Update(MonsterContext ctx)
    {
        float dt = Time.deltaTime;
        _timer -= dt;

        switch (_phase)
        {
            // ── 준비 ──────────────────────────────────────
            case Phase.WindUp:
                FacePlayer(ctx);
                if (_timer <= 0f)
                {
                    // 1타 경고 원 (짧게)
                    _indicator?.ShowCircle(ctx.Transform, Data.hit1Radius, Data.hit1Duration * 0.6f);
                    _phase = Phase.Hit1;
                    _timer = Data.hit1Duration;
                }
                break;

            // ── 1타 ───────────────────────────────────────
            case Phase.Hit1:
            {
                float elapsed = Data.hit1Duration - _timer;
                if (!_hit1Dealt && elapsed >= Data.hitTime1)
                {
                    _hit1Dealt = true;
                    _indicator?.HideCircle();
                    _bb.AudioPool?.Play(ctx.Transform.position, Data.hit1Sfx, 0.6f);
                    DealDamage(ctx, Data.hit1Radius, Data.hit1DamageMul);
                }
                if (_timer <= 0f)
                {
                    ctx.Animator?.CrossFade(Data.hit2AnimState, 0.05f);
                    _phase = Phase.Step;
                    _timer = 0.1f;
                }
                break;
            }

            // ── 전진 스텝 ─────────────────────────────────
            case Phase.Step:
                StepTowardPlayer(ctx, Data.stepDistance, dt);
                if (_timer <= 0f)
                {
                    FacePlayer(ctx);
                    _indicator?.ShowCircle(ctx.Transform, Data.hit2Radius, Data.hit2Duration * 0.55f);
                    _phase = Phase.Hit2;
                    _timer = Data.hit2Duration;
                }
                break;

            // ── 2타 ───────────────────────────────────────
            case Phase.Hit2:
            {
                float elapsed = Data.hit2Duration - _timer;
                if (!_hit2Dealt && elapsed >= Data.hitTime2)
                {
                    _hit2Dealt = true;
                    _indicator?.HideCircle();
                    _bb.AudioPool?.Play(ctx.Transform.position, Data.hit2Sfx, 0.7f);
                    DealDamage(ctx, Data.hit2Radius, Data.hit2DamageMul);
                }
                if (_timer <= 0f)
                {
                    // 각성 시 3타 추가
                    if (_bb.HasEnraged)
                    {
                        ctx.Animator?.CrossFade(Data.hit3AnimState, 0.05f);
                        _phase = Phase.StepFwd2;
                        _timer = 0.08f;
                    }
                    else
                    {
                        _phase = Phase.Done;
                    }
                }
                break;
            }

            // ── 2차 전진 스텝 (각성 전용) ──────────────────
            case Phase.StepFwd2:
                StepTowardPlayer(ctx, Data.stepDistance * 0.7f, dt);
                if (_timer <= 0f)
                {
                    FacePlayer(ctx);
                    _indicator?.ShowCircle(ctx.Transform, Data.hit3Radius, Data.hit3Duration * 0.5f);
                    _phase = Phase.Hit3;
                    _timer = Data.hit3Duration;
                }
                break;

            // ── 3타 (각성 전용) ────────────────────────────
            case Phase.Hit3:
            {
                float elapsed = Data.hit3Duration - _timer;
                if (!_hit3Dealt && elapsed >= Data.hitTime3)
                {
                    _hit3Dealt = true;
                    _indicator?.HideCircle();
                    _bb.AudioPool?.Play(ctx.Transform.position, Data.hit3Sfx, 0.8f);
                    DealDamage(ctx, Data.hit3Radius, Data.hit3DamageMul);
                }
                if (_timer <= 0f)
                    _phase = Phase.Done;
                break;
            }

            case Phase.Done:
                ctx.Monster.ChangeState<ChaseState>();
                break;
        }
    }

    public override void Exit(MonsterContext ctx)
    {
        _indicator?.HideCircle();
        _bb.PressureCooldown = Data.pressureCooldown;

        // NavMesh 복구 (Step 중 비활성화된 경우)
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

    // ── 헬퍼 ─────────────────────────────────────────────

    private void DealDamage(MonsterContext ctx, float radius, float damageMul)
    {
        var cols = Physics.OverlapSphere(ctx.Transform.position, radius);
        foreach (var col in cols)
        {
            var player = col.GetComponent<PlayerController>()
                      ?? col.GetComponentInParent<PlayerController>();
            if (player == null) continue;

            int dmg = Mathf.RoundToInt(ctx.Config.stat.attackPower
                                       * damageMul
                                       * ctx.Runtime.AttackMultiplier);
            player.TakeDamage(dmg);

            var rb = col.GetComponent<Rigidbody>() ?? col.GetComponentInParent<Rigidbody>();
            if (rb != null)
            {
                Vector3 dir = (col.transform.position - ctx.Transform.position).normalized;
                dir.y = Data.knockbackY;
                rb.AddForce(dir.normalized * ctx.Config.stat.knockbackForce * Data.knockbackMul,
                            ForceMode.Impulse);
            }
            break;
        }
    }

    private void StepTowardPlayer(MonsterContext ctx, float dist, float dt)
    {
        if (ctx.Runtime.PlayerTarget == null) return;
        Vector3 dir = (ctx.Runtime.PlayerTarget.position - ctx.Transform.position);
        dir.y = 0f;
        if (dir.sqrMagnitude < 0.001f) return;

        float speed = dist / 0.1f; // 0.1s 동안 dist 만큼
        Vector3 next = ctx.Transform.position + dir.normalized * speed * dt;
        next.y = _startY;
        ctx.Transform.position = next;
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
