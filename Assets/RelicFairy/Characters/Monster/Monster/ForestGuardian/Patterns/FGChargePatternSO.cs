using UnityEngine;
using UnityEngine.AI;

namespace RelicFairy.Monster
{
/// <summary>
/// ForestGuardian 돌진 — 멀어진 플레이어에게 거리를 좁히는 패턴.
/// (09-28 보스 전수 검증: 5 m 밖에서는 원거리 패턴 하나만 제자리에서 되풀이하고 다가오지 않았다.)
///
/// 조건  : 플레이어가 minRange 이상 떨어져 있을 때(원거리 엔트리).
/// 흐름  : Windup — 플레이어를 향해 돌며 돌진 줄 가이드가 차오르고, 마지막 lockDuration 동안은 방향이 고정된다(판정 색)
///        → Charge — 고정된 줄을 따라 chargeSpeed로 돌진(NavMesh 위로만 — 벽을 뚫지 않는다). 몸에 닿으면 한 번 피해·넉백
///        → Recovery → ChaseState.
/// 회피  : 방향이 고정된 뒤 옆으로 비키면 피한다(돌진 중 추적 없음).
/// 이동  : FullLock — 경직·중단 불가. 이동은 이 상태가 NavMeshAgent.Move로 직접 한다.
/// </summary>
[CreateAssetMenu(menuName = "RelicFairy/Boss/ForestGuardian/FG_ChargePattern", fileName = "FG_ChargePattern")]
public class FGChargePatternSO : BossPatternSO
{
    // ── 조건 ──────────────────────────────────────────────
    [Header("Charge — Condition")]
    [Tooltip("이 거리 이상 떨어졌을 때만 발동 (m)")]
    public float minRange = 9f;

    [Tooltip("이 거리를 넘으면 발동하지 않음 (m)")]
    public float maxRange = 40f;

    // ── 타이밍 ────────────────────────────────────────────
    [Header("Charge — Timing")]
    [Tooltip("돌진 전 준비(가이드가 차오르는) 시간 (초)")]
    public float windupDuration = 1.0f;

    [Tooltip("준비 끝 무렵 방향을 고정하는 시간 (초) — 이 동안 옆으로 비키면 피한다")]
    public float lockDuration = 0.45f;

    [Tooltip("준비 중 플레이어를 향해 도는 속도 (°/s)")]
    public float windupTurnSpeed = 240f;

    [Tooltip("돌진 뒤 자세 복귀 시간 (초)")]
    public float recoveryDuration = 0.7f;

    // ── 이동 ──────────────────────────────────────────────
    [Header("Charge — Movement")]
    [Tooltip("돌진 속도 (m/s)")]
    public float chargeSpeed = 16f;

    [Tooltip("목표(준비 끝의 플레이어 위치) 너머로 더 가는 거리 (m)")]
    public float overshoot = 3f;

    [Tooltip("벽·아레나 가장자리 앞에서 멈추는 여유 (m)")]
    public float stopBeforeEdge = 1.5f;

    [Tooltip("가이드·판정 폭 (m)")]
    public float width = 2.6f;

    // ── 데미지 ────────────────────────────────────────────
    [Header("Charge — Damage")]
    [Tooltip("기본 attackPower에 곱할 배율")]
    public float damageMultiplier = 1.3f;

    [Tooltip("넉백 힘 배율")]
    public float knockbackMultiplier = 1.6f;

    [Tooltip("몸에 닿았다고 보는 수평 반경 (m)")]
    public float hitRadius = 1.8f;

    // ── 런타임 ───────────────────────────────────────────
    private FGChargeState _state;

    public override void Initialize(BossPatternContext ctx) => _state = new FGChargeState(this);
    public override void OnRecycled() => _state = new FGChargeState(this);

    public override bool CanExecute(BossPatternContext ctx)
    {
        if (ctx.Ctx.Runtime.PlayerTarget == null) return false;
        Vector3 d = ctx.Ctx.Runtime.PlayerTarget.position - ctx.Ctx.Transform.position;
        d.y = 0f;
        float dist = d.magnitude;
        return dist >= minRange && dist <= maxRange;
    }

    public override SpecialStateBase GetRuntimeState() => _state;
}

// ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
// FGChargeState — FullLock (중단 불가 + 이동은 상태가 직접)
// ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

public class FGChargeState : FullLockState<FGChargePatternSO>
{
    private const string AnimReady  = "AttackReady";
    private const string AnimCharge = "Charge";

    private enum Phase { Windup, Charge, Recovery }

    private Phase      _phase;
    private float      _timer;
    private bool       _locked;
    private bool       _hasHit;
    private Vector3    _dir;
    private float      _length;
    private float      _moved;
    private GameObject _guideGO;

    public FGChargeState(FGChargePatternSO data) : base(data) { }

    public override void Enter(MonsterContext ctx)
    {
        _phase  = Phase.Windup;
        _timer  = 0f;
        _locked = false;
        _hasHit = false;
        _moved  = 0f;

        if (ctx.Agent != null && ctx.Agent.isOnNavMesh)
        {
            ctx.Agent.isStopped = true;
            ctx.Agent.ResetPath();
        }

        FacePlayer(ctx, 360f);
        Aim(ctx);
        _guideGO = PatternGuideHelper.Prepare(
            PatternGuideHelper.Beam(ctx.Transform.position, _dir, _length, Data.width, PatternGuideHelper.Telegraph),
            ForestGuardianMonster.GuideFlow);
        PlayAnim(ctx, AnimReady);
    }

    public override void Update(MonsterContext ctx)
    {
        float dt = Time.deltaTime * SpeedMult(ctx);
        _timer += dt;

        switch (_phase)
        {
            // ── Windup : 플레이어를 향해 돌며 줄이 차오른다 — 끝 무렵 방향 고정 ──
            case Phase.Windup:
                if (!_locked)
                {
                    FacePlayer(ctx, Data.windupTurnSpeed * Time.deltaTime);
                    Aim(ctx);
                    PatternGuideHelper.PlaceBeam(_guideGO, ctx.Transform.position, _dir, _length, Data.width);
                    if (_timer >= Data.windupDuration - Data.lockDuration)
                    {
                        _locked = true;
                        PatternGuideHelper.Arm(_guideGO);   // 이제 방향이 안 바뀐다 — 비킬 때
                    }
                }
                if (Data.windupDuration > 0f)
                    PatternGuideHelper.SetProgress(_guideGO, _timer / Data.windupDuration);

                if (_timer >= Data.windupDuration)
                {
                    _phase = Phase.Charge;
                    _timer = 0f;
                    PlayAnim(ctx, AnimCharge);
                }
                break;

            // ── Charge : 고정된 줄을 따라 돌진 ──
            case Phase.Charge:
            {
                float step = Data.chargeSpeed * dt;
                if (_moved + step > _length) step = Mathf.Max(0f, _length - _moved);
                Vector3 before = ctx.Transform.position;
                if (ctx.Agent != null && ctx.Agent.isOnNavMesh) ctx.Agent.Move(_dir * step);
                else ctx.Transform.position += _dir * step;
                float actual = Vector3.Distance(Flat(before), Flat(ctx.Transform.position));
                _moved += step;

                if (!_hasHit) TryHit(ctx);

                // 목표 거리 도달 · NavMesh 가장자리에 막힘(실제 이동이 크게 줄어듦) → 멈춤
                bool blocked = step > 0.05f && actual < step * 0.3f;
                if (_moved >= _length - 0.01f || blocked)
                {
                    _phase = Phase.Recovery;
                    _timer = 0f;
                    PatternGuideHelper.SafeDestroy(ref _guideGO);
                    PlayAnim(ctx, AnimReady);
                }
                break;
            }

            // ── Recovery ──
            case Phase.Recovery:
                if (_timer >= Data.recoveryDuration)
                    ctx.Monster.ChangeState<ChaseState>();
                break;
        }
    }

    public override void Exit(MonsterContext ctx)
    {
        PatternGuideHelper.SafeDestroy(ref _guideGO);
        if (ctx.Agent != null && ctx.Agent.isOnNavMesh)
        {
            ctx.Agent.isStopped = false;
            ctx.Agent.Warp(ctx.Transform.position);
        }
    }

    // ── 조준 — 방향과 돌진 길이(플레이어 너머 overshoot, NavMesh 가장자리 앞에서 멈춤) ──
    private void Aim(MonsterContext ctx)
    {
        Vector3 from = ctx.Transform.position;
        Vector3 to   = ctx.Runtime.PlayerTarget != null ? ctx.Runtime.PlayerTarget.position : from + ctx.Transform.forward * Data.minRange;
        Vector3 d    = Flat(to - from);
        _dir = d.sqrMagnitude > 0.001f ? d.normalized : Flat(ctx.Transform.forward).normalized;
        _length = d.magnitude + Data.overshoot;

        if (NavMesh.Raycast(from, from + _dir * _length, out var hit, NavMesh.AllAreas))
            _length = Mathf.Max(0f, hit.distance - Data.stopBeforeEdge);
    }

    private void TryHit(MonsterContext ctx)
    {
        if (ctx.Config?.stat == null || ctx.Runtime.PlayerTarget == null) return;
        Vector3 toPlayer = Flat(ctx.Runtime.PlayerTarget.position - ctx.Transform.position);
        if (toPlayer.magnitude > Data.hitRadius) return;

        var player = ctx.Runtime.PlayerTarget.GetComponent<PlayerController>();
        if (player == null) return;
        _hasHit = true;

        int dmg = Mathf.Max(1, (int)(ctx.Config.stat.attackPower * Data.damageMultiplier));
        player.TakeDamage(dmg, ctx.Monster.gameObject, false, HitWeight.Heavy);

        // 돌진 방향 옆으로 밀어낸다 — 앞으로 밀면 같은 줄에 계속 끌려간다
        Vector3 side = Vector3.Cross(Vector3.up, _dir);
        if (Vector3.Dot(side, toPlayer) < 0f) side = -side;
        Vector3 knock = (side + _dir * 0.5f).normalized;
        knock.y = 0.3f;
        player.ApplyKnockback(knock.normalized * ctx.Config.stat.knockbackForce * Data.knockbackMultiplier);
    }

    private static void FacePlayer(MonsterContext ctx, float maxDegrees)
    {
        if (ctx.Runtime.PlayerTarget == null) return;
        Vector3 dir = Flat(ctx.Runtime.PlayerTarget.position - ctx.Transform.position);
        if (dir.sqrMagnitude < 0.001f) return;
        ctx.Transform.rotation = Quaternion.RotateTowards(ctx.Transform.rotation, Quaternion.LookRotation(dir), maxDegrees);
    }

    private static Vector3 Flat(Vector3 v) { v.y = 0f; return v; }

    private static void PlayAnim(MonsterContext ctx, string stateName)
    {
        if (ctx.Animator == null) return;
        if (!ctx.Animator.HasState(0, Animator.StringToHash(stateName)))
        {
            Debug.LogWarning($"[FGCharge] Animator state not found: '{stateName}'", ctx.Monster);
            return;
        }
        ctx.Animator.speed = SpeedMult(ctx);
        ctx.Animator.CrossFade(stateName, 0.1f, 0, 0f);
    }

    private static float SpeedMult(MonsterContext ctx)
    {
        var fg = ctx.Monster as ForestGuardianMonster;
        return fg?.FGBlackboard.AnimSpeedMult ?? 1f;
    }
}
}
