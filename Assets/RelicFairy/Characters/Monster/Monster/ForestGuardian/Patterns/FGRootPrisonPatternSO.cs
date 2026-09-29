using UnityEngine;

namespace RelicFairy.Monster
{
/// <summary>
/// ForestGuardian 2페이지 FL1 「뿌리 감옥」 (09-28 설계 확정 §3).
///
/// 조건  : 플레이어가 maxRange 이내(가까이 · 멀리 풀 공용 — R2 「플레이어 자리에 감옥」).
/// 흐름  : Cast — 시전 순간 플레이어 자리를 감옥 중심으로 고정. 안쪽 원 예고가 차오르고, 틈 2곳을 흰 화살표로 표시
///        → Rise — 둘레(반경 ringRadius)에 뿌리 벽이 솟는다(물리 벽 — 틈 말고는 못 지나간다)
///        → Slam — 솟은 뒤 trapDuration 초에 안쪽 전체가 내려찍힌다 → 벽이 가라앉고 Recovery → ChaseState.
/// 회피  : 틈으로 빠져나간다. 중심은 시전 순간 고정 — 추적 없음. 판정 armLead 초 전에 예고가 판정 색으로 바뀐다.
/// 시간  : 실시간(2페이즈 애니 배속과 무관) — 설계 수치(1.6초)를 그대로 지킨다.
/// </summary>
[CreateAssetMenu(menuName = "RelicFairy/Boss/ForestGuardian/FG_RootPrisonPattern", fileName = "FG_RootPrisonPattern")]
public class FGRootPrisonPatternSO : BossPatternSO
{
    // ── 조건 ──────────────────────────────────────────────
    [Header("RootPrison — Condition")]
    [Tooltip("이 거리 이내일 때 발동 (m)")]
    public float maxRange = 12f;

    // ── 감옥 ──────────────────────────────────────────────
    [Header("RootPrison — Ring")]
    [Tooltip("뿌리 벽 반경 — 플레이어 둘레 (m)")]
    public float ringRadius = 4f;

    [Tooltip("틈 하나의 폭 (m) — 두 곳")]
    public float gapWidth = 2.4f;

    [Tooltip("벽 한 토막 길이 (m)")]
    public float wallSegmentLength = 1.3f;

    [Tooltip("벽 높이 (m)")]
    public float wallHeight = 2.4f;

    [Tooltip("벽 두께 (m)")]
    public float wallThickness = 0.7f;

    [Tooltip("두 번째 틈이 첫 틈의 정반대에서 벗어나는 최대 각 (도)")]
    public float gapJitterDegrees = 35f;

    // ── 타이밍 ────────────────────────────────────────────
    [Header("RootPrison — Timing (실시간 초)")]
    [Tooltip("시전 → 벽이 솟을 때까지 (VerticalAttack 내려찍는 순간)")]
    public float castDuration = 0.7f;

    [Tooltip("벽이 솟은 뒤 안쪽이 내려찍힐 때까지 — 이 안에 틈으로 나간다")]
    public float trapDuration = 1.6f;

    [Tooltip("판정 이만큼 전에 예고를 판정 색으로 (R3)")]
    public float armLead = 0.4f;

    [Tooltip("내려찍은 뒤 벽이 남아 있는 시간")]
    public float wallLinger = 0.4f;

    [Tooltip("벽이 가라앉은 뒤 자세 복귀 시간")]
    public float recoveryDuration = 0.6f;

    // ── 데미지 ────────────────────────────────────────────
    [Header("RootPrison — Damage")]
    [Tooltip("기본 attackPower 배율")]
    public float damageMultiplier = 1.4f;

    [Tooltip("넉백 힘 배율 (감옥 중심에서 바깥으로)")]
    public float knockbackMultiplier = 1.5f;

    // ── 비주얼 · 사운드 ───────────────────────────────────
    [Header("RootPrison — Visual")]
    [Tooltip("벽 한 토막마다 솟는 가시 뿌리 이펙트(시각 전용). 비우면 이펙트 없이 벽만")]
    public GameObject wallVfxPrefab;
    public float      wallVfxScale = 1f;

    [Tooltip("안쪽이 내려찍힐 때 이펙트(감옥 중심)")]
    public GameObject slamVfxPrefab;
    public float      slamVfxScale = 4f;

    [Header("RootPrison — Sound")]
    public AudioClip riseSfx;
    public AudioClip slamSfx;

    // ── 런타임 ───────────────────────────────────────────
    private FGRootPrisonState _state;

    public override void Initialize(BossPatternContext ctx) => _state = new FGRootPrisonState(this);
    public override void OnRecycled() => _state = new FGRootPrisonState(this);

    public override bool CanExecute(BossPatternContext ctx)
    {
        if (ctx.Ctx.Runtime.PlayerTarget == null) return false;
        return Vector3.Distance(ctx.Ctx.Transform.position, ctx.Ctx.Runtime.PlayerTarget.position) <= maxRange;
    }

    public override SpecialStateBase GetRuntimeState() => _state;
}

// ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
// FGRootPrisonState — FullLock (보스 제자리 · 중단 불가)
// ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

public class FGRootPrisonState : FullLockState<FGRootPrisonPatternSO>
{
    private const string AnimCast  = "VerticalAttack";
    private const string AnimReady = "AttackReady";
    private const int    GapCount  = 2;
    private const float  VfxLifetime = 3f;

    private enum Phase { Cast, Trapped, Linger, Recovery }

    private Phase      _phase;
    private float      _timer;
    private bool       _armed;
    private Vector3    _center;
    private GameObject _insideGO;
    private GameObject _wallsRoot;
    private readonly float[]      _gapYaw   = new float[GapCount];
    private readonly GameObject[] _gapGuide = new GameObject[GapCount];

    public FGRootPrisonState(FGRootPrisonPatternSO data) : base(data) { }

    public override void Enter(MonsterContext ctx)
    {
        _phase = Phase.Cast;
        _timer = 0f;
        _armed = false;

        if (ctx.Agent != null && ctx.Agent.isOnNavMesh)
        {
            ctx.Agent.isStopped = true;
            ctx.Agent.ResetPath();
        }

        FacePlayer(ctx);

        // 감옥 중심은 시전 순간의 플레이어 자리 — 이후 따라가지 않는다
        _center = ctx.Runtime.PlayerTarget != null ? ctx.Runtime.PlayerTarget.position : ctx.Transform.position + ctx.Transform.forward * 3f;
        _gapYaw[0] = Random.Range(0f, 360f);
        _gapYaw[1] = _gapYaw[0] + 180f + Random.Range(-Data.gapJitterDegrees, Data.gapJitterDegrees);

        _insideGO = PatternGuideHelper.Prepare(
            PatternGuideHelper.Disc(_center, Data.ringRadius, PatternGuideHelper.Telegraph), ForestGuardianMonster.GuideFlow);
        for (int i = 0; i < GapCount; i++)
            _gapGuide[i] = SpawnGapArrow(_gapYaw[i]);

        PlayAnim(ctx, AnimCast);
    }

    public override void Update(MonsterContext ctx)
    {
        _timer += Time.deltaTime;
        float slamAt = Data.castDuration + Data.trapDuration;

        switch (_phase)
        {
            // ── Cast : 내려찍는 모션 — 안쪽 예고가 차오른다 ──
            case Phase.Cast:
                UpdateInsideGuide(slamAt);
                if (_timer >= Data.castDuration)
                {
                    RaiseWalls();
                    PlayAnim(ctx, AnimReady);
                    _phase = Phase.Trapped;
                }
                break;

            // ── Trapped : 벽이 솟아 있다 — 틈으로 나갈 시간 ──
            case Phase.Trapped:
                UpdateInsideGuide(slamAt);
                if (_timer >= slamAt)
                {
                    Slam(ctx);
                    _phase = Phase.Linger;
                }
                break;

            case Phase.Linger:
                if (_timer >= slamAt + Data.wallLinger)
                {
                    DestroyWalls();
                    _phase = Phase.Recovery;
                }
                break;

            case Phase.Recovery:
                if (_timer >= slamAt + Data.wallLinger + Data.recoveryDuration)
                    ctx.Monster.ChangeState<ChaseState>();
                break;
        }
    }

    public override void Exit(MonsterContext ctx)
    {
        DestroyGuides();
        DestroyWalls();

        if (ctx.Agent != null && ctx.Agent.isOnNavMesh)
            ctx.Agent.isStopped = false;
    }

    // ── 예고 ─────────────────────────────────────────────
    private void UpdateInsideGuide(float slamAt)
    {
        if (slamAt > 0f) PatternGuideHelper.SetProgress(_insideGO, _timer / slamAt);
        if (!_armed && _timer >= slamAt - Data.armLead)
        {
            _armed = true;
            PatternGuideHelper.Arm(_insideGO);   // 곧 내려찍는다 — 안쪽이 판정 색
        }
    }

    /// <summary>틈 표시 — 안에서 밖으로 향하는 흰 화살표(「여기로 나간다」).</summary>
    private GameObject SpawnGapArrow(float yaw)
    {
        Vector3 dir    = Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;
        Vector3 origin = _center + dir * Mathf.Max(0f, Data.ringRadius - 1.4f);
        var go = PatternGuideHelper.Beam(origin, dir, 2.8f, Data.gapWidth * 0.7f, ForestGuardianMonster.GuideSafe);
        PatternGuideHelper.SetFlow(go, ForestGuardianMonster.GuideSafe);
        PatternGuideHelper.SetProgress(go, 1f);
        return go;
    }

    private void DestroyGuides()
    {
        PatternGuideHelper.SafeDestroy(ref _insideGO);
        for (int i = 0; i < GapCount; i++)
            PatternGuideHelper.SafeDestroy(ref _gapGuide[i]);
    }

    // ── 뿌리 벽 ─────────────────────────────────────────
    /// <summary>둘레를 토막 벽(BoxCollider, Default 레이어 — 기사 피라미드 베기 방벽과 같은 방식)으로 두른다. 틈 두 곳은 비운다.</summary>
    private void RaiseWalls()
    {
        DestroyWalls();
        _wallsRoot = new GameObject("~FG_RootPrison");
        _wallsRoot.transform.position = _center;

        float r         = Mathf.Max(0.5f, Data.ringRadius);
        int   count     = Mathf.Max(6, Mathf.CeilToInt(2f * Mathf.PI * r / Mathf.Max(0.3f, Data.wallSegmentLength)));
        float step      = 360f / count;
        float segLen    = 2f * Mathf.PI * r / count;
        float gapHalf   = (Data.gapWidth * 0.5f + segLen * 0.5f) / r * Mathf.Rad2Deg;   // 토막이 틈에 걸치지 않게

        for (int i = 0; i < count; i++)
        {
            float yaw = i * step;
            if (InGap(yaw, gapHalf)) continue;

            Vector3    dir = Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;
            Quaternion rot = Quaternion.LookRotation(dir);

            var wall = new GameObject("RootWall");
            wall.transform.SetParent(_wallsRoot.transform, false);
            wall.transform.SetPositionAndRotation(_center + dir * r + Vector3.up * (Data.wallHeight * 0.5f), rot);
            var box = wall.AddComponent<BoxCollider>();
            box.size = new Vector3(segLen * 1.05f, Data.wallHeight, Data.wallThickness);   // x = 둘레 방향 · z = 반지름 방향

            if (Data.wallVfxPrefab != null)
            {
                var fx = Object.Instantiate(Data.wallVfxPrefab, _center + dir * r, rot, _wallsRoot.transform);
                fx.transform.localScale = Vector3.one * Data.wallVfxScale;
                foreach (var col in fx.GetComponentsInChildren<Collider>()) col.enabled = false;   // 시각 전용
            }
        }
        Physics.SyncTransforms();

        if (Data.riseSfx != null)
            Managers.Sound?.PlayEffectAt(Data.riseSfx, _center);
    }

    private bool InGap(float yaw, float gapHalf)
    {
        for (int i = 0; i < GapCount; i++)
            if (Mathf.Abs(Mathf.DeltaAngle(yaw, _gapYaw[i])) <= gapHalf) return true;
        return false;
    }

    private void DestroyWalls()
    {
        if (_wallsRoot == null) return;
        Object.Destroy(_wallsRoot);
        _wallsRoot = null;
    }

    // ── 내려찍기 ─────────────────────────────────────────
    private void Slam(MonsterContext ctx)
    {
        DestroyGuides();
        SpawnOneShot(Data.slamVfxPrefab, _center, Data.slamVfxScale);
        if (Data.slamSfx != null)
            Managers.Sound?.PlayEffectAt(Data.slamSfx, _center);

        var player = ctx.Runtime.CachedPlayer;
        if (ctx.Config?.stat == null || player == null || ctx.Runtime.PlayerTarget == null) return;

        Vector3 d = ctx.Runtime.PlayerTarget.position - _center;
        d.y = 0f;
        if (d.magnitude > Data.ringRadius) return;   // 틈으로 나갔다

        int dmg = Mathf.Max(1, (int)(ctx.Config.stat.attackPower * Data.damageMultiplier));
        player.TakeDamage(dmg, ctx.Monster.gameObject, false, HitWeight.Heavy);

        Vector3 knock = d.sqrMagnitude > 0.001f ? d.normalized : ctx.Transform.forward;
        knock.y = 0.3f;
        player.ApplyKnockback(knock.normalized * ctx.Config.stat.knockbackForce * Data.knockbackMultiplier);
    }

    private static void SpawnOneShot(GameObject prefab, Vector3 pos, float scale)
    {
        if (prefab == null) return;
        var go = Managers.ObjectPooler.SpawnFromPrefab(prefab, ObjectPoolerManager.PoolType.Effect, pos, Quaternion.identity);
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
            Debug.LogWarning($"[FGRootPrison] Animator state not found: '{stateName}'", ctx.Monster);
            return;
        }
        ctx.Animator.speed = 1f;
        ctx.Animator.CrossFade(stateName, 0.1f, 0, 0f);
    }
}
}
