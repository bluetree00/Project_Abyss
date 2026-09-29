using UnityEngine;

namespace RelicFairy.Monster
{
/// <summary>
/// ForestGuardian 2페이지 FL4 「뿌리 회오리」 (09-28 설계 확정 §3).
///
/// 조건  : 플레이어가 maxRange 이내(실제 판정은 outerRadius까지 — R6).
/// 흐름  : Windup — 보스 둘레 outerRadius 원 전체가 예고되고, 그 위 안쪽 원(innerRadius)이 먼저 차오른다
///        → 360 회전 발차기 — 안쪽 원(0~inner) 판정 · 안쪽이 흰 원(안전)으로 바뀐다
///        → outerDelay 초 뒤 바깥 고리(inner~outer) 판정 → Recovery → ChaseState.
/// 회피  : 안 → 밖 또는 밖 → 안으로 한 번 옮긴다(안쪽이 터진 뒤 흰 원 안은 안전).
/// 시간  : 실시간(2페이즈 애니 배속과 무관). 원은 보스 자리에 고정 — 추적 없음. 각 판정 armLead 초 전에 판정 색.
/// </summary>
[CreateAssetMenu(menuName = "RelicFairy/Boss/ForestGuardian/FG_RootWhirlPattern", fileName = "FG_RootWhirlPattern")]
public class FGRootWhirlPatternSO : BossPatternSO
{
    // ── 조건 ──────────────────────────────────────────────
    [Header("RootWhirl — Condition")]
    [Tooltip("이 거리 이내일 때 발동 (m) — 바깥 고리(outerRadius)보다 짧게")]
    public float maxRange = 9f;

    // ── 범위 ──────────────────────────────────────────────
    [Header("RootWhirl — Range")]
    [Tooltip("안쪽 원 반경 (m) — 먼저 터진다")]
    public float innerRadius = 5f;

    [Tooltip("바깥 고리 바깥 반경 (m) — 안쪽 뒤에 터진다")]
    public float outerRadius = 10f;

    // ── 타이밍 ────────────────────────────────────────────
    [Header("RootWhirl — Timing (실시간 초)")]
    [Tooltip("준비(AttackReady) 시간 — 끝나면 360 회전 발차기")]
    public float windupDuration = 0.55f;

    [Tooltip("회전 발차기 시작 → 안쪽 원 판정 — 360SpinKick 클립은 왼발이 0.83~0.92초에 가장 멀리·높이 뻗는다(FBX 실측 09-28)")]
    public float spinHitDelay = 0.85f;

    [Tooltip("안쪽 판정 → 바깥 고리 판정")]
    public float outerDelay = 0.6f;

    [Tooltip("각 판정 이만큼 전에 예고를 판정 색으로")]
    public float armLead = 0.4f;

    [Tooltip("바깥 판정 뒤 자세 복귀 시간")]
    public float recoveryDuration = 0.8f;

    // ── 데미지 ────────────────────────────────────────────
    [Header("RootWhirl — Damage")]
    [Tooltip("안쪽 원 피해 = attackPower × 이 배율")]
    public float innerDamageMultiplier = 1.3f;

    [Tooltip("바깥 고리 피해 = attackPower × 이 배율")]
    public float outerDamageMultiplier = 1.2f;

    [Tooltip("넉백 힘 배율 (보스에서 바깥으로)")]
    public float knockbackMultiplier = 1.5f;

    // ── 비주얼 · 사운드 ───────────────────────────────────
    [Header("RootWhirl — Visual")]
    [Tooltip("안쪽 원이 터질 때 이펙트(보스 발밑)")]
    public GameObject innerVfxPrefab;
    public float      innerVfxScale = 5f;

    [Tooltip("바깥 고리가 터질 때 고리를 따라 늘어놓는 이펙트")]
    public GameObject outerVfxPrefab;
    public float      outerVfxScale = 1.2f;
    [Tooltip("바깥 고리 이펙트 개수")]
    public int        outerVfxCount = 10;

    [Header("RootWhirl — Sound")]
    public AudioClip spinSfx;
    public AudioClip outerSfx;

    // ── 런타임 ───────────────────────────────────────────
    private FGRootWhirlState _state;

    public override void Initialize(BossPatternContext ctx) => _state = new FGRootWhirlState(this);
    public override void OnRecycled() => _state = new FGRootWhirlState(this);

    public override bool CanExecute(BossPatternContext ctx)
    {
        if (ctx.Ctx.Runtime.PlayerTarget == null) return false;
        return Vector3.Distance(ctx.Ctx.Transform.position, ctx.Ctx.Runtime.PlayerTarget.position) <= maxRange;
    }

    public override SpecialStateBase GetRuntimeState() => _state;
}

// ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
// FGRootWhirlState — FullLock (보스 제자리 · 중단 불가)
// ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

public class FGRootWhirlState : FullLockState<FGRootWhirlPatternSO>
{
    private const string AnimReady = "AttackReady";
    private const string AnimSpin  = "360SpinKick";
    private const float  LayerLift = 0.02f;   // 바깥 원 위에 겹쳐 그리는 원 — 같은 높이면 깜빡인다
    private const float  VfxLifetime = 3f;

    private enum Phase { Windup, Spin, Outer, Recovery }

    private Phase      _phase;
    private float      _timer;
    private bool       _innerArmed;
    private bool       _outerArmed;
    private Vector3    _center;
    private GameObject _outerGO;
    private GameObject _innerGO;
    private GameObject _safeGO;

    public FGRootWhirlState(FGRootWhirlPatternSO data) : base(data) { }

    public override void Enter(MonsterContext ctx)
    {
        _phase      = Phase.Windup;
        _timer      = 0f;
        _innerArmed = false;
        _outerArmed = false;

        if (ctx.Agent != null && ctx.Agent.isOnNavMesh)
        {
            ctx.Agent.isStopped = true;
            ctx.Agent.ResetPath();
        }

        FacePlayer(ctx);
        _center = ctx.Transform.position;

        // 바깥 원(전체 위험 범위) 아래 · 안쪽 원 위 — 안쪽이 먼저 차오른다
        _outerGO = PatternGuideHelper.Prepare(
            PatternGuideHelper.Disc(_center, Data.outerRadius, PatternGuideHelper.Telegraph), ForestGuardianMonster.GuideFlow);
        _innerGO = PatternGuideHelper.Prepare(
            PatternGuideHelper.Disc(_center + Vector3.up * LayerLift, Data.innerRadius, PatternGuideHelper.Telegraph), ForestGuardianMonster.GuideFlow);

        PlayAnim(ctx, AnimReady);
    }

    public override void Update(MonsterContext ctx)
    {
        _timer += Time.deltaTime;
        float innerAt = Data.windupDuration + Data.spinHitDelay;
        float outerAt = innerAt + Data.outerDelay;

        if (innerAt > 0f) PatternGuideHelper.SetProgress(_innerGO, _timer / innerAt);
        if (outerAt > 0f) PatternGuideHelper.SetProgress(_outerGO, _timer / outerAt);
        if (!_innerArmed && _timer >= innerAt - Data.armLead)
        {
            _innerArmed = true;
            PatternGuideHelper.Arm(_innerGO);
        }
        if (!_outerArmed && _timer >= outerAt - Data.armLead)
        {
            _outerArmed = true;
            PatternGuideHelper.Arm(_outerGO);
        }

        switch (_phase)
        {
            case Phase.Windup:
                if (_timer >= Data.windupDuration)
                {
                    PlayAnim(ctx, AnimSpin);
                    if (Data.spinSfx != null)
                        Managers.Sound?.PlayEffectAt(Data.spinSfx, _center);
                    _phase = Phase.Spin;
                }
                break;

            // ── 안쪽 원 판정 → 안쪽은 흰 원(안전) ──
            case Phase.Spin:
                if (_timer >= innerAt)
                {
                    SpawnOneShot(Data.innerVfxPrefab, _center, Data.innerVfxScale);
                    TryHit(ctx, 0f, Data.innerRadius, Data.innerDamageMultiplier);

                    PatternGuideHelper.SafeDestroy(ref _innerGO);
                    _safeGO = PatternGuideHelper.Disc(_center + Vector3.up * LayerLift, Data.innerRadius, ForestGuardianMonster.GuideSafe);
                    PatternGuideHelper.SetFlow(_safeGO, ForestGuardianMonster.GuideSafe);
                    PatternGuideHelper.SetProgress(_safeGO, 1f);
                    _phase = Phase.Outer;
                }
                break;

            // ── 바깥 고리 판정 ──
            case Phase.Outer:
                if (_timer >= outerAt)
                {
                    SpawnOuterVfx();
                    if (Data.outerSfx != null)
                        Managers.Sound?.PlayEffectAt(Data.outerSfx, _center);
                    TryHit(ctx, Data.innerRadius, Data.outerRadius, Data.outerDamageMultiplier);

                    DestroyGuides();
                    PlayAnim(ctx, AnimReady);
                    _phase = Phase.Recovery;
                }
                break;

            case Phase.Recovery:
                if (_timer >= outerAt + Data.recoveryDuration)
                    ctx.Monster.ChangeState<ChaseState>();
                break;
        }
    }

    public override void Exit(MonsterContext ctx)
    {
        DestroyGuides();

        if (ctx.Agent != null && ctx.Agent.isOnNavMesh)
            ctx.Agent.isStopped = false;
    }

    private void DestroyGuides()
    {
        PatternGuideHelper.SafeDestroy(ref _outerGO);
        PatternGuideHelper.SafeDestroy(ref _innerGO);
        PatternGuideHelper.SafeDestroy(ref _safeGO);
    }

    /// <summary>수평 거리가 (min, max] — 안쪽 원은 min 0부터(보스 몸 위 포함).</summary>
    private void TryHit(MonsterContext ctx, float minRadius, float maxRadius, float damageMult)
    {
        var player = ctx.Runtime.CachedPlayer;
        if (ctx.Config?.stat == null || player == null || ctx.Runtime.PlayerTarget == null) return;

        Vector3 d = ctx.Runtime.PlayerTarget.position - _center;
        d.y = 0f;
        float dist = d.magnitude;
        if (dist > maxRadius || (minRadius > 0f && dist <= minRadius)) return;

        int dmg = Mathf.Max(1, (int)(ctx.Config.stat.attackPower * damageMult));
        player.TakeDamage(dmg, ctx.Monster.gameObject, false, HitWeight.Heavy);

        Vector3 knock = dist > 0.001f ? d / dist : ctx.Transform.forward;
        knock.y = 0.3f;
        player.ApplyKnockback(knock.normalized * ctx.Config.stat.knockbackForce * Data.knockbackMultiplier);
    }

    private void SpawnOuterVfx()
    {
        if (Data.outerVfxPrefab == null) return;
        int   n   = Mathf.Max(1, Data.outerVfxCount);
        float mid = (Data.innerRadius + Data.outerRadius) * 0.5f;
        for (int i = 0; i < n; i++)
        {
            Vector3 dir = Quaternion.Euler(0f, i * 360f / n, 0f) * Vector3.forward;
            SpawnOneShot(Data.outerVfxPrefab, _center + dir * mid, Data.outerVfxScale, Quaternion.LookRotation(dir));
        }
    }

    private static void SpawnOneShot(GameObject prefab, Vector3 pos, float scale) => SpawnOneShot(prefab, pos, scale, Quaternion.identity);

    private static void SpawnOneShot(GameObject prefab, Vector3 pos, float scale, Quaternion rot)
    {
        if (prefab == null) return;
        var go = Managers.ObjectPooler.SpawnFromPrefab(prefab, ObjectPoolerManager.PoolType.Effect, pos, rot);
        go.transform.localScale = Vector3.one * scale;
        if (!go.TryGetComponent<PooledOneShotVfx>(out var vfx)) vfx = go.AddComponent<PooledOneShotVfx>();
        vfx.Play(VfxLifetime);
    }

    // ── 유틸 ─────────────────────────────────────────────
    private static void FacePlayer(MonsterContext ctx)
    {
        if (ctx.Runtime.PlayerTarget == null) return;
        Vector3 dir = ctx.Runtime.PlayerTarget.position - ctx.Transform.position;
        dir.y = 0f;
        if (dir.sqrMagnitude > 0.001f)
            ctx.Transform.rotation = Quaternion.LookRotation(dir);
    }

    private static void PlayAnim(MonsterContext ctx, string stateName)
    {
        if (ctx.Animator == null) return;
        if (!ctx.Animator.HasState(0, Animator.StringToHash(stateName)))
        {
            Debug.LogWarning($"[FGRootWhirl] Animator state not found: '{stateName}'", ctx.Monster);
            return;
        }
        ctx.Animator.speed = 1f;
        ctx.Animator.CrossFade(stateName, 0.1f, 0, 0f);
    }
}
}
