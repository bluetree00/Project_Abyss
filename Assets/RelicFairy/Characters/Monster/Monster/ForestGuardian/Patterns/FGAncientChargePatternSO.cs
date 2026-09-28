using UnityEngine;
using UnityEngine.AI;

namespace RelicFairy.Monster
{
/// <summary>
/// ForestGuardian 2페이지 FL2 「고목의 돌진 3연」 (09-28 설계 확정 §3) — 1페이지 돌진(FGChargePatternSO)의 강화.
///
/// 조건  : 플레이어가 minRange 이상 떨어져 있을 때(R2 — 멀어지면 다가간다).
/// 흐름  : Windup — 플레이어를 향해 돌며 돌진 줄 예고가 차오르고, 끝 lockDuration 동안 방향 고정(판정 색)
///        → Charge — 고정된 줄을 따라 돌진(NavMesh 위로만). 몸에 닿으면 한 번 피해 · 옆으로 넉백
///        → 다시 조준(두 번째부터 followWindup으로 짧게) · 돌진 — chargeCount번 → Recovery → ChaseState.
/// 회피  : 방향이 고정된 뒤 옆으로 비킨다. 고정은 R3대로 돌진 시작 lockDuration(≥0.4초) 전.
/// 시간  : 실시간(2페이즈 애니 배속과 무관) — 1페이지 돌진은 배속을 따라 2페이즈에서 고정이 0.4초 밑으로 짧아진다.
/// </summary>
[CreateAssetMenu(menuName = "RelicFairy/Boss/ForestGuardian/FG_AncientChargePattern", fileName = "FG_AncientChargePattern")]
public class FGAncientChargePatternSO : BossPatternSO
{
    // ── 조건 ──────────────────────────────────────────────
    [Header("AncientCharge — Condition")]
    [Tooltip("이 거리 이상 떨어졌을 때만 발동 (m)")]
    public float minRange = 8f;

    [Tooltip("이 거리를 넘으면 발동하지 않음 (m)")]
    public float maxRange = 40f;

    // ── 타이밍 ────────────────────────────────────────────
    [Header("AncientCharge — Timing (실시간 초)")]
    [Tooltip("돌진 횟수")]
    public int chargeCount = 3;

    [Tooltip("첫 돌진 준비(조준) 시간")]
    public float firstWindup = 0.8f;

    [Tooltip("두 번째부터의 준비 시간 — 짧다")]
    public float followWindup = 0.55f;

    [Tooltip("준비 끝 무렵 방향을 고정하는 시간 — 이 동안 옆으로 비키면 피한다(R3 ≥ 0.4)")]
    public float lockDuration = 0.4f;

    [Tooltip("준비 중 플레이어를 향해 도는 속도 (°/s)")]
    public float windupTurnSpeed = 300f;

    [Tooltip("마지막 돌진 뒤 자세 복귀 시간")]
    public float recoveryDuration = 0.8f;

    // ── 이동 ──────────────────────────────────────────────
    [Header("AncientCharge — Movement")]
    [Tooltip("돌진 속도 (m/s)")]
    public float chargeSpeed = 17f;

    [Tooltip("목표(고정 순간의 플레이어 위치) 너머로 더 가는 거리 (m)")]
    public float overshoot = 3f;

    [Tooltip("벽·아레나 가장자리 앞에서 멈추는 여유 (m)")]
    public float stopBeforeEdge = 1.5f;

    [Tooltip("가이드·판정 폭 (m)")]
    public float width = 2.6f;

    // ── 데미지 ────────────────────────────────────────────
    [Header("AncientCharge — Damage")]
    [Tooltip("기본 attackPower에 곱할 배율 (돌진 한 번당)")]
    public float damageMultiplier = 1.2f;

    [Tooltip("넉백 힘 배율")]
    public float knockbackMultiplier = 1.6f;

    [Tooltip("몸에 닿았다고 보는 수평 반경 (m)")]
    public float hitRadius = 1.8f;

    [Header("AncientCharge — Sound")]
    [Tooltip("돌진 시작마다 재생")]
    public AudioClip chargeSfx;

    // ── 런타임 ───────────────────────────────────────────
    private FGAncientChargeState _state;

    public override void Initialize(BossPatternContext ctx) => _state = new FGAncientChargeState(this);
    public override void OnRecycled() => _state = new FGAncientChargeState(this);

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
// FGAncientChargeState — FullLock (중단 불가 + 이동은 상태가 직접)
// ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

public class FGAncientChargeState : FullLockState<FGAncientChargePatternSO>
{
    private const string AnimReady  = "AttackReady";
    private const string AnimCharge = "Charge";

    private enum Phase { Windup, Charge, Recovery }

    private Phase      _phase;
    private float      _timer;
    private float      _windup;
    private int        _index;
    private bool       _locked;
    private bool       _hasHit;
    private Vector3    _dir;
    private float      _length;
    private float      _moved;
    private GameObject _guideGO;

    public FGAncientChargeState(FGAncientChargePatternSO data) : base(data) { }

    public override void Enter(MonsterContext ctx)
    {
        _index = 0;

        if (ctx.Agent != null && ctx.Agent.isOnNavMesh)
        {
            ctx.Agent.isStopped = true;
            ctx.Agent.ResetPath();
        }

        BeginWindup(ctx, Data.firstWindup);
    }

    public override void Update(MonsterContext ctx)
    {
        float dt = Time.deltaTime;
        _timer += dt;

        switch (_phase)
        {
            // ── Windup : 플레이어를 향해 돌며 줄이 차오른다 — 끝 lockDuration 동안 방향 고정 ──
            case Phase.Windup:
                if (!_locked)
                {
                    FacePlayer(ctx, Data.windupTurnSpeed * dt);
                    Aim(ctx);
                    PatternGuideHelper.PlaceBeam(_guideGO, ctx.Transform.position, _dir, _length, Data.width);
                    if (_timer >= _windup - Data.lockDuration)
                    {
                        _locked = true;
                        PatternGuideHelper.Arm(_guideGO);   // 이제 방향이 안 바뀐다 — 비킬 때
                    }
                }
                if (_windup > 0f)
                    PatternGuideHelper.SetProgress(_guideGO, _timer / _windup);

                if (_timer >= _windup)
                {
                    _phase = Phase.Charge;
                    _timer = 0f;
                    PlayAnim(ctx, AnimCharge);
                    if (Data.chargeSfx != null)
                        Managers.Sound?.PlayEffectAt(Data.chargeSfx, ctx.Transform.position);
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

                // 목표 거리 도달 · NavMesh 가장자리에 막힘 → 다음 돌진 또는 복귀
                bool blocked = step > 0.05f && actual < step * 0.3f;
                if (_moved >= _length - 0.01f || blocked)
                {
                    _index++;
                    if (_index < Data.chargeCount)
                        BeginWindup(ctx, Data.followWindup);
                    else
                    {
                        _phase = Phase.Recovery;
                        _timer = 0f;
                        PatternGuideHelper.SafeDestroy(ref _guideGO);
                        PlayAnim(ctx, AnimReady);
                    }
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

    // ── 한 번의 조준 시작 — 새 줄 예고 ──
    private void BeginWindup(MonsterContext ctx, float windup)
    {
        _phase  = Phase.Windup;
        _timer  = 0f;
        _windup = Mathf.Max(windup, Data.lockDuration);   // 고정 구간이 준비보다 길 수 없다
        _locked = false;
        _hasHit = false;
        _moved  = 0f;

        FacePlayer(ctx, 360f);
        Aim(ctx);
        PatternGuideHelper.SafeDestroy(ref _guideGO);
        _guideGO = PatternGuideHelper.Prepare(
            PatternGuideHelper.Beam(ctx.Transform.position, _dir, _length, Data.width, PatternGuideHelper.Telegraph),
            ForestGuardianMonster.GuideFlow);
        PlayAnim(ctx, AnimReady);
    }

    // ── 조준 — 방향과 돌진 길이(플레이어 너머 overshoot, NavMesh 가장자리 앞에서 멈춤) ──
    private void Aim(MonsterContext ctx)
    {
        Vector3 from = ctx.Transform.position;
        Vector3 to   = ctx.Runtime.PlayerTarget != null ? ctx.Runtime.PlayerTarget.position : from + ctx.Transform.forward * Data.minRange;
        Vector3 d    = Flat(to - from);
        _dir    = d.sqrMagnitude > 0.001f ? d.normalized : Flat(ctx.Transform.forward).normalized;
        _length = d.magnitude + Data.overshoot;

        if (NavMesh.Raycast(from, from + _dir * _length, out var hit, NavMesh.AllAreas))
            _length = Mathf.Max(0f, hit.distance - Data.stopBeforeEdge);
    }

    private void TryHit(MonsterContext ctx)
    {
        var player = ctx.Runtime.CachedPlayer;
        if (ctx.Config?.stat == null || player == null || ctx.Runtime.PlayerTarget == null) return;
        Vector3 toPlayer = Flat(ctx.Runtime.PlayerTarget.position - ctx.Transform.position);
        if (toPlayer.magnitude > Data.hitRadius) return;

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
            Debug.LogWarning($"[FGAncientCharge] Animator state not found: '{stateName}'", ctx.Monster);
            return;
        }
        ctx.Animator.speed = 1f;
        ctx.Animator.CrossFade(stateName, 0.1f, 0, 0f);
    }
}
}
