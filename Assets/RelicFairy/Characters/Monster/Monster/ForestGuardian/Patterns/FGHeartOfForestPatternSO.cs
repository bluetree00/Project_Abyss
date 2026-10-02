using UnityEngine;

namespace RelicFairy.Monster
{
/// <summary>
/// ForestGuardian 2페이지 간판 FL-S 「숲의 심장」 (09-28 설계 확정 §3 · §6).
///
/// 조건  : Page_SignatureDue(2페이지 체력 50% · 아직 안 씀), forceExecute — 한 전투에 한 번.
/// 흐름  : Move — 아레나 가운데로 걸어간다(최대 moveMaxSeconds)
///        → Channel(channelDuration) — 뿌리박고 채널링. 가장자리 가시가 안쪽으로 차오른다(임시 가시 띠 — 영구 띠와 같은 피해).
///          발밑 청록 원 = 칠 수 있는 약점(심장). 플레이어가 몸을 hitsRequired번 치면(실제로 체력이 깎인 타격만) 성공.
///        → 성공: 채널링이 끊기고 그로기 groggyDuration초(받는 피해 +groggyDamageTakenAmp) — GetHitState(그로기)로 넘긴다.
///        → 실패: 전역 충격 — 플레이어 최대 체력 × shockMaxHpRatio(즉사하지 않게 체력 1은 남긴다) + 가시 띠 영구 +widenOnFail m.
/// 제약  : FullLock(중단 불가 · 이동은 상태가 직접). **무적 아님** — 약점을 쳐야 하는 창이다.
/// 시간  : 실시간(2페이즈 애니 배속과 무관).
/// </summary>
[CreateAssetMenu(menuName = "RelicFairy/Boss/ForestGuardian/FG_HeartOfForestPattern", fileName = "FG_HeartOfForestPattern")]
public class FGHeartOfForestPatternSO : BossPatternSO
{
    // ── 이동 ──────────────────────────────────────────────
    [Header("Heart — Move to Center")]
    [Tooltip("가운데로 걸어가는 속도 (m/s)")]
    public float moveSpeed = 8f;

    [Tooltip("가운데로 걸어가는 최대 시간 — 넘으면 그 자리에서 뿌리박는다")]
    public float moveMaxSeconds = 2.5f;

    [Tooltip("이만큼 가까우면 도착 (m)")]
    public float arriveDistance = 0.8f;

    // ── 채널링 ────────────────────────────────────────────
    [Header("Heart — Channel (실시간 초)")]
    [Tooltip("채널링 시간 — 이 안에 약점을 친다")]
    public float channelDuration = 12f;

    [Tooltip("성공에 필요한 타격 수(한 번 휘두름의 다단 판정은 한 대)")]
    public int hitsRequired = 5;

    [Tooltip("가시가 차오른 끝에 가운데 남는 사각형의 반 폭 (m) — 가시 띠가 이 안쪽까지 자란다")]
    public float creepInnerHalfSize = 5.5f;

    [Tooltip("충격 이만큼 전에 전역 경고(판정 색)")]
    public float shockWarning = 1.2f;

    [Tooltip("발밑 약점 원 반경 (m)")]
    public float heartMarkerRadius = 2.8f;

    // ── 성공 ──────────────────────────────────────────────
    [Header("Heart — Success")]
    [Tooltip("그로기 시간")]
    public float groggyDuration = 4f;

    [Tooltip("그로기 동안 받는 피해 증가(0.3 = +30%)")]
    public float groggyDamageTakenAmp = 0.3f;

    // ── 실패 ──────────────────────────────────────────────
    [Header("Heart — Failure")]
    [Tooltip("전역 충격 = 플레이어 최대 체력 × 이 비율(즉사하지 않는다)")]
    public float shockMaxHpRatio = 0.35f;

    [Tooltip("실패 시 가시 띠를 영구히 넓히는 폭 (m, 한 번)")]
    public float widenOnFail = 1.5f;

    [Tooltip("충격 뒤 자세 복귀 시간")]
    public float recoveryDuration = 1.3f;

    // ── 비주얼 · 사운드 ───────────────────────────────────
    [Header("Heart — Visual")]
    [Tooltip("채널링 동안 보스에 붙여 도는 이펙트(시각 전용). 비우면 없음")]
    public GameObject channelVfxPrefab;
    public float      channelVfxScale = 1f;

    [Tooltip("전역 충격 이펙트(보스 자리)")]
    public GameObject shockVfxPrefab;
    public float      shockVfxScale = 8f;

    [Tooltip("약점을 다 쳐 채널링이 끊길 때 이펙트(보스 자리)")]
    public GameObject breakVfxPrefab;
    public float      breakVfxScale = 3f;

    [Header("Heart — Sound")]
    public AudioClip channelStartSfx;
    public AudioClip heartHitSfx;
    public AudioClip shockSfx;
    public AudioClip breakSfx;

    // ── 런타임 ───────────────────────────────────────────
    private FGHeartOfForestState _state;

    public override void Initialize(BossPatternContext ctx) => _state = new FGHeartOfForestState(this);
    public override void OnRecycled() => _state = new FGHeartOfForestState(this);

    public override bool CanExecute(BossPatternContext ctx)
        => ctx?.Ctx?.Monster is IPagedBoss paged && paged.Pages != null && paged.Pages.SignatureDue(ctx.Ctx.Monster.CurrentHp);

    public override SpecialStateBase GetRuntimeState() => _state;
}

// ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
// FGHeartOfForestState — FullLock (중단 불가, 무적 아님)
// ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

public class FGHeartOfForestState : FullLockState<FGHeartOfForestPatternSO>
{
    private const string AnimWalk         = "Walk";
    private const string AnimChannelStart = "MagicAttackS";
    private const string AnimChannelLoop  = "MagicAttackL";
    private const string AnimRelease      = "MagicAttackE";
    private const float  ChannelStartTime = 0.75f;   // MagicAttackS(23프레임) 뒤 루프로
    private const float  VfxLifetime      = 3.5f;

    private enum Phase { Move, Channel, Recovery }

    private Phase           _phase;
    private float           _timer;
    private bool            _looping;
    private int             _hitStart;
    private int             _lastHits;
    private Vector3         _target;
    private GameObject      _heartGO;
    private GameObject      _shockGO;
    private GameObject      _channelFx;
    private BossStageHazard _creep;

    public FGHeartOfForestState(FGHeartOfForestPatternSO data) : base(data) { }

    public override void Enter(MonsterContext ctx)
    {
        _phase   = Phase.Move;
        _timer   = 0f;
        _looping = false;

        var fg = ctx.Monster as ForestGuardianMonster;
        (ctx.Monster as IPagedBoss)?.Pages?.MarkSignatureDone();   // 끊기더라도 다시 쓰지 않는다 — 한 전투에 한 번
        if (fg != null) fg.ClearChargeTrails();   // 악몽 특성 「흔적」 — 간판은 깨끗한 바닥에서

        if (ctx.Agent != null && ctx.Agent.isOnNavMesh)
        {
            ctx.Agent.isStopped = true;
            ctx.Agent.ResetPath();
        }

        _target = fg != null ? fg.ArenaCenter : ctx.Transform.position;
        PlayAnim(ctx, AnimWalk, 1.4f);
        Debug.Log($"[FG] 간판 「숲의 심장」 시작 — HP={ctx.Monster.CurrentHp}/{ctx.Monster.EffectiveMaxHp}", ctx.Monster);
    }

    public override void Update(MonsterContext ctx)
    {
        float dt = Time.deltaTime;
        _timer += dt;

        switch (_phase)
        {
            // ── Move : 아레나 가운데로 ──
            case Phase.Move:
            {
                Vector3 to   = Flat(_target - ctx.Transform.position);
                float   dist = to.magnitude;
                if (dist <= Data.arriveDistance || _timer >= Data.moveMaxSeconds)
                {
                    BeginChannel(ctx);
                    break;
                }
                Vector3 dir  = to / dist;
                float   step = Mathf.Min(Data.moveSpeed * dt, dist);
                if (ctx.Agent != null && ctx.Agent.isOnNavMesh) ctx.Agent.Move(dir * step);
                else ctx.Transform.position += dir * step;
                ctx.Transform.rotation = Quaternion.RotateTowards(ctx.Transform.rotation, Quaternion.LookRotation(dir), 540f * dt);
                break;
            }

            // ── Channel : 약점을 치는 창 ──
            case Phase.Channel:
                TickChannelAnim(ctx);

                int hits = ForestGuardianHits(ctx) - _hitStart;
                if (hits != _lastHits)
                {
                    _lastHits = hits;
                    if (Data.hitsRequired > 0)
                        PatternGuideHelper.SetProgress(_heartGO, (float)hits / Data.hitsRequired);
                    if (Data.heartHitSfx != null)
                        Managers.Sound?.PlayEffectAt(Data.heartHitSfx, ctx.Transform.position);
                }
                if (hits >= Data.hitsRequired)
                {
                    Succeed(ctx);
                    return;   // 그로기(GetHitState)로 넘어갔다
                }

                if (_shockGO == null && _timer >= Data.channelDuration - Data.shockWarning)
                    SpawnShockWarning(ctx);
                if (_timer >= Data.channelDuration)
                    Fail(ctx);
                break;

            // ── Recovery : 충격 뒤 ──
            case Phase.Recovery:
                if (_timer >= Data.recoveryDuration)
                {
                    if (ctx.Monster is IPagedBoss paged) paged.ReturnToCombat();
                    else ctx.Monster.ChangeState<ChaseState>();
                }
                break;
        }
    }

    public override void Exit(MonsterContext ctx)
    {
        ClearChannel();

        if (ctx.Agent != null && ctx.Agent.isOnNavMesh)
        {
            ctx.Agent.isStopped = false;
            ctx.Agent.Warp(ctx.Transform.position);
        }
    }

    // ── 채널링 시작 — 약점 표시 · 가시가 안쪽으로 ──
    private void BeginChannel(MonsterContext ctx)
    {
        _phase    = Phase.Channel;
        _timer    = 0f;
        _hitStart = ForestGuardianHits(ctx);
        _lastHits = 0;

        FacePlayer(ctx);
        PlayAnim(ctx, AnimChannelStart, 1f);

        // 발밑 청록 원 — 「칠 수 있다」. 친 만큼 차오른다.
        _heartGO = PatternGuideHelper.Prepare(
            PatternGuideHelper.Disc(ctx.Transform.position, Data.heartMarkerRadius, PatternGuideHelper.Breakable), PatternGuideHelper.Breakable);

        if (ctx.Monster is ForestGuardianMonster fg)
        {
            // 가장자리 가시가 채널링 동안 안쪽으로 차오른다 — 영구 띠와 같은 색 · 피해의 임시 띠(끝나면 걷는다)
            Bounds arena = fg.ArenaBoundsXZ;
            float  half  = Mathf.Min(arena.extents.x, arena.extents.z);
            float  band  = Mathf.Max(0.5f, half - Data.creepInnerHalfSize);
            _creep = BossStageHazard.CreateEdgeBand(fg, arena, fg.ArenaFloorY, band, Data.channelDuration,
                                                    fg.ThornBandColor, null, 1f, fg.ThornBandDamageMult);
            fg.NotifyVulnerableWindow(Data.channelDuration);
        }

        if (Data.channelVfxPrefab != null)
        {
            _channelFx = Object.Instantiate(Data.channelVfxPrefab, ctx.Transform.position, Quaternion.identity, ctx.Transform);
            _channelFx.transform.localScale = Vector3.one * Data.channelVfxScale;
        }
        if (Data.channelStartSfx != null)
            Managers.Sound?.PlayEffectAt(Data.channelStartSfx, ctx.Transform.position);
    }

    private void TickChannelAnim(MonsterContext ctx)
    {
        if (ctx.Animator == null) return;
        if (!_looping)
        {
            if (_timer < ChannelStartTime) return;
            _looping = true;
            PlayAnim(ctx, AnimChannelLoop, 1f);
            return;
        }
        // 클립 루프 (normalizedTime >= 1 에서 재시작) — 브레스와 같은 방식
        var si = ctx.Animator.GetCurrentAnimatorStateInfo(0);
        if (si.IsName(AnimChannelLoop) && si.normalizedTime >= 1f)
            ctx.Animator.Play(AnimChannelLoop, 0, 0f);
    }

    /// <summary>전역 충격 예고 — 아레나 전체를 덮는 판정 색 원.</summary>
    private void SpawnShockWarning(MonsterContext ctx)
    {
        float radius = 20f;
        if (ctx.Monster is ForestGuardianMonster fg)
            radius = fg.ArenaBoundsXZ.extents.magnitude;
        _shockGO = PatternGuideHelper.Disc(ctx.Transform.position, radius, PatternGuideHelper.Active);
        PatternGuideHelper.Arm(_shockGO);
    }

    // ── 성공 — 채널링이 끊기고 그로기 ──
    private void Succeed(MonsterContext ctx)
    {
        ClearChannel();
        SpawnOneShot(Data.breakVfxPrefab, ctx.Transform.position, Data.breakVfxScale);
        if (Data.breakSfx != null)
            Managers.Sound?.PlayEffectAt(Data.breakSfx, ctx.Transform.position);

        Debug.Log($"[FG] 간판 「숲의 심장」 성공 — 그로기 {Data.groggyDuration:F1}초", ctx.Monster);
        if (ctx.Monster is ForestGuardianMonster fg)
        {
            fg.BeginHeartGroggy(Data.groggyDuration, Data.groggyDamageTakenAmp);
            ctx.Monster.ChangeState<GetHitState>();   // FGGetHitState — 그로기 모션, 그로기가 풀릴 때까지
        }
        else
            ctx.Monster.ChangeState<ChaseState>();
    }

    // ── 실패 — 전역 충격 + 가시 띠 영구 확장 ──
    private void Fail(MonsterContext ctx)
    {
        ClearChannel();
        PlayAnim(ctx, AnimRelease, 1f);
        SpawnOneShot(Data.shockVfxPrefab, ctx.Transform.position, Data.shockVfxScale);
        if (Data.shockSfx != null)
            Managers.Sound?.PlayEffectAt(Data.shockSfx, ctx.Transform.position);
        BossImpactFeedback.TriggerCameraShake(0.14f, 0.45f);

        var player = ctx.Runtime.CachedPlayer;
        if (player != null && player.RuntimeStats != null)
        {
            // 최대 체력 비율 피해 — 방어로 다시 깎이지 않게(10-01) · 체력 1은 남긴다(즉사 없음, 설계 §6)
            int want = BossMaxHpDamage.Raw(player, Data.shockMaxHpRatio);
            int dmg  = BossMaxHpDamage.NonLethal(player, want);
            if (dmg > 0) player.TakeDamage(dmg, ctx.Monster.gameObject, false, HitWeight.Heavy);
        }

        (ctx.Monster as ForestGuardianMonster)?.WidenThornBand(Data.widenOnFail);
        Debug.Log($"[FG] 간판 「숲의 심장」 실패 — 전역 충격 · 가시 띠 +{Data.widenOnFail:F1} m", ctx.Monster);

        _timer = 0f;
        _phase = Phase.Recovery;
    }

    private void ClearChannel()
    {
        PatternGuideHelper.SafeDestroy(ref _heartGO);
        PatternGuideHelper.SafeDestroy(ref _shockGO);
        if (_creep != null) Object.Destroy(_creep.gameObject);
        _creep = null;
        if (_channelFx != null) Object.Destroy(_channelFx);
        _channelFx = null;
    }

    private static int ForestGuardianHits(MonsterContext ctx)
        => ctx.Monster is ForestGuardianMonster fg ? fg.DirectHitCount : 0;

    private static void SpawnOneShot(GameObject prefab, Vector3 pos, float scale)
    {
        if (prefab == null) return;
        var go = Managers.ObjectPooler.SpawnFromPrefab(prefab, ObjectPoolerManager.PoolType.Effect, pos, Quaternion.identity);
        go.transform.localScale = Vector3.one * scale;
        if (!go.TryGetComponent<PooledOneShotVfx>(out var vfx)) vfx = go.AddComponent<PooledOneShotVfx>();
        vfx.Play(VfxLifetime);
    }

    // ── 유틸 ─────────────────────────────────────────────
    private static Vector3 Flat(Vector3 v) { v.y = 0f; return v; }

    private static void FacePlayer(MonsterContext ctx)
    {
        if (ctx.Runtime.PlayerTarget == null) return;
        Vector3 dir = Flat(ctx.Runtime.PlayerTarget.position - ctx.Transform.position);
        if (dir.sqrMagnitude > 0.001f)
            ctx.Transform.rotation = Quaternion.LookRotation(dir);
    }

    private static void PlayAnim(MonsterContext ctx, string stateName, float speed)
    {
        if (ctx.Animator == null) return;
        if (!ctx.Animator.HasState(0, Animator.StringToHash(stateName)))
        {
            Debug.LogWarning($"[FGHeartOfForest] Animator state not found: '{stateName}'", ctx.Monster);
            return;
        }
        ctx.Animator.speed = speed;
        ctx.Animator.CrossFade(stateName, 0.1f, 0, 0f);
    }
}
}
