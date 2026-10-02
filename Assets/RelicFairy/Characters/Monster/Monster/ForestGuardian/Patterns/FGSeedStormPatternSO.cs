using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace RelicFairy.Monster
{
/// <summary>
/// ForestGuardian 2페이지 FL3 「씨앗 폭우」 (09-28 설계 확정 §3).
///
/// 조건  : 플레이어가 minRange 이상 떨어져 있을 때.
/// 흐름  : Gather — 두 팔을 모은다(MagicAttackS) → Throw — 씨앗 seedCount개를 흩뿌린다(MagicAttackE):
///        하나는 플레이어 자리, 나머지는 플레이어 둘레 고리에 고르게. 떨어질 자리마다 원 예고가 flightTime 동안 차오른다
///        → Land — 원 안이면 피해 한 번 · 자리마다 가시덤불(FGThornBush)이 bushLifetime 초 남는다(동시 최대 maxBushes개,
///        넘치면 가장 오래된 것부터 없앤다) → Recovery → ChaseState.
/// 회피  : 원 밖으로 · 덤불 사이 길을 읽는다. 떨어질 자리는 던지는 순간 고정 — 추적 없음.
/// 시간  : 실시간(2페이즈 애니 배속과 무관).
/// </summary>
[CreateAssetMenu(menuName = "RelicFairy/Boss/ForestGuardian/FG_SeedStormPattern", fileName = "FG_SeedStormPattern")]
public class FGSeedStormPatternSO : BossPatternSO
{
    // ── 조건 ──────────────────────────────────────────────
    [Header("SeedStorm — Condition")]
    [Tooltip("최소 발동 거리 (m)")]
    public float minRange = 5f;

    [Tooltip("최대 발동 거리 (m)")]
    public float maxRange = 25f;

    // ── 씨앗 ──────────────────────────────────────────────
    [Header("SeedStorm — Seeds")]
    [Tooltip("한 번에 뿌리는 씨앗 수 — 하나는 플레이어 자리, 나머지는 둘레")]
    public int seedCount = 5;

    [Tooltip("둘레 씨앗이 떨어질 고리 — 플레이어에서 최소 거리 (m)")]
    public float ringMin = 2.5f;

    [Tooltip("둘레 씨앗이 떨어질 고리 — 플레이어에서 최대 거리 (m)")]
    public float ringMax = 4.5f;

    [Tooltip("둘레 씨앗 방위 흔들림 (도)")]
    public float angleJitter = 20f;

    [Tooltip("떨어질 자리 원 반경 = 착지 피해 반경 (m)")]
    public float landRadius = 1.6f;

    [Tooltip("씨앗이 뜨는 높이 — 포물선 꼭대기 (m)")]
    public float arcHeight = 4f;

    // ── 타이밍 ────────────────────────────────────────────
    [Header("SeedStorm — Timing (실시간 초)")]
    [Tooltip("팔을 모으는 시간 → 던진다")]
    public float gatherDuration = 0.6f;

    [Tooltip("떨어질 자리 원 예고 = 씨앗 비행 시간")]
    public float flightTime = 1.2f;

    [Tooltip("착지 이만큼 전에 예고를 판정 색으로")]
    public float armLead = 0.4f;

    [Tooltip("착지 뒤 자세 복귀 시간")]
    public float recoveryDuration = 0.7f;

    // ── 가시덤불 ──────────────────────────────────────────
    [Header("SeedStorm — Thorn Bush")]
    [Tooltip("덤불이 남는 시간")]
    public float bushLifetime = 8f;

    [Tooltip("동시에 남아 있을 수 있는 덤불 수(넘치면 오래된 것부터 사라진다)")]
    public int maxBushes = 6;

    [Tooltip("덤불 반경 (m)")]
    public float bushRadius = 1.5f;

    [Tooltip("덤불 피해 주기 (초)")]
    public float bushTickSeconds = 0.5f;

    [Tooltip("덤불 틱 피해 = attackPower × 이 배율")]
    public float bushDamageMultiplier = 0.15f;

    [Tooltip("덤불 바닥 색(반투명)")]
    public Color bushColor = new Color(0.45f, 0.12f, 0.06f, 0.55f);

    // ── 데미지 ────────────────────────────────────────────
    [Header("SeedStorm — Damage")]
    [Tooltip("착지 피해 = attackPower × 이 배율 (한 번 던질 때 한 번만)")]
    public float impactDamageMultiplier = 1.0f;

    [Tooltip("착지 넉백 힘 배율")]
    public float knockbackMultiplier = 1f;

    // ── 비주얼 · 사운드 ───────────────────────────────────
    [Header("SeedStorm — Visual")]
    [Tooltip("날아가는 씨앗(시각 전용 — 이동 스크립트 없는 프리팹). 비우면 원 예고만")]
    public GameObject seedPrefab;
    public float      seedScale = 1f;

    [Tooltip("착지 이펙트")]
    public GameObject impactVfxPrefab;
    public float      impactVfxScale = 1f;

    [Tooltip("덤불 이펙트(시각 전용). 비우면 바닥 원만")]
    public GameObject bushVfxPrefab;
    public float      bushVfxScale = 1f;

    [Header("SeedStorm — Sound")]
    public AudioClip throwSfx;
    public AudioClip landSfx;

    // ── 런타임 ───────────────────────────────────────────
    private FGSeedStormState _state;

    public override void Initialize(BossPatternContext ctx) => _state = new FGSeedStormState(this);
    public override void OnRecycled() => _state = new FGSeedStormState(this);

    public override bool CanExecute(BossPatternContext ctx)
    {
        if (ctx.Ctx.Runtime.PlayerTarget == null) return false;
        float dist = Vector3.Distance(ctx.Ctx.Transform.position, ctx.Ctx.Runtime.PlayerTarget.position);
        return dist >= minRange && dist <= maxRange;
    }

    public override SpecialStateBase GetRuntimeState() => _state;
}

// ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
// FGSeedStormState — FullLock (보스 제자리 · 중단 불가)
// ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

public class FGSeedStormState : FullLockState<FGSeedStormPatternSO>
{
    private const string AnimGather = "MagicAttackS";
    private const string AnimThrow  = "MagicAttackE";
    private const float  FaceSpeed  = 360f;
    private const float  VfxLifetime = 2.5f;
    private const float  SeedAppearSeconds = 0.25f;   // 씨앗이 디졸브로 맺히는 시간(10-03)
    private const float  SeedVanishSeconds = 0.3f;    // 떨어진 씨앗이 디졸브로 꺼지는 시간(10-03)

    private enum Phase { Gather, Flight, Recovery }

    private Phase   _phase;
    private float   _timer;
    private bool    _armed;
    private Vector3 _origin;
    private float   _seedsShownAt;   // 씨앗 등장 디졸브 시작 시각 — 끝나기 전에 치우면 디졸브 없이 없앤다
    private readonly int          _count;
    private readonly Vector3[]    _targets;
    private readonly GameObject[] _discs;
    private readonly GameObject[] _seeds;
    private readonly List<FGThornBush> _bushes = new();   // 이 보스가 남긴 덤불 — 상태보다 오래 산다

    public FGSeedStormState(FGSeedStormPatternSO data) : base(data)
    {
        _count   = Mathf.Max(1, data.seedCount);
        _targets = new Vector3[_count];
        _discs   = new GameObject[_count];
        _seeds   = new GameObject[_count];
    }

    public override void Enter(MonsterContext ctx)
    {
        _phase = Phase.Gather;
        _timer = 0f;
        _armed = false;

        if (ctx.Agent != null && ctx.Agent.isOnNavMesh)
        {
            ctx.Agent.isStopped = true;
            ctx.Agent.ResetPath();
        }

        FacePlayer(ctx, 360f);
        PlayAnim(ctx, AnimGather);
    }

    public override void Update(MonsterContext ctx)
    {
        _timer += Time.deltaTime;

        switch (_phase)
        {
            // ── Gather : 플레이어 쪽으로 돌며 팔을 모은다 ──
            case Phase.Gather:
                FacePlayer(ctx, FaceSpeed * Time.deltaTime);
                if (_timer >= Data.gatherDuration)
                {
                    Throw(ctx);
                    _timer = 0f;
                    _phase = Phase.Flight;
                }
                break;

            // ── Flight : 씨앗이 포물선으로 날아가고 원 예고가 차오른다 ──
            case Phase.Flight:
            {
                float t = Data.flightTime > 0f ? Mathf.Clamp01(_timer / Data.flightTime) : 1f;
                for (int i = 0; i < _count; i++)
                {
                    PatternGuideHelper.SetProgress(_discs[i], t);
                    if (_seeds[i] != null)
                        _seeds[i].transform.position = Vector3.Lerp(_origin, _targets[i], t) + Vector3.up * (Data.arcHeight * 4f * t * (1f - t));
                }
                if (!_armed && _timer >= Data.flightTime - Data.armLead)
                {
                    _armed = true;
                    for (int i = 0; i < _count; i++) PatternGuideHelper.Arm(_discs[i]);
                }
                if (_timer >= Data.flightTime)
                {
                    Land(ctx);
                    _timer = 0f;
                    _phase = Phase.Recovery;
                }
                break;
            }

            case Phase.Recovery:
                if (_timer >= Data.recoveryDuration)
                    ctx.Monster.ChangeState<ChaseState>();
                break;
        }
    }

    public override void Exit(MonsterContext ctx)
    {
        ClearFlight();

        if (ctx.Agent != null && ctx.Agent.isOnNavMesh)
            ctx.Agent.isStopped = false;
    }

    // ── 던지기 — 떨어질 자리를 지금 고정한다 ──
    private void Throw(MonsterContext ctx)
    {
        ClearFlight();
        PlayAnim(ctx, AnimThrow);
        if (Data.throwSfx != null)
            Managers.Sound?.PlayEffectAt(Data.throwSfx, ctx.Transform.position);

        _origin = ctx.Transform.position + Vector3.up * 3f + ctx.Transform.forward * 1f;
        Vector3 center = ctx.Runtime.PlayerTarget != null ? ctx.Runtime.PlayerTarget.position : ctx.Transform.position + ctx.Transform.forward * Data.minRange;

        float baseYaw = Random.Range(0f, 360f);
        int   around  = _count - 1;
        for (int i = 0; i < _count; i++)
        {
            Vector3 p = center;
            if (i > 0)
            {
                float yaw = baseYaw + (i - 1) * (360f / Mathf.Max(1, around)) + Random.Range(-Data.angleJitter, Data.angleJitter);
                p += Quaternion.Euler(0f, yaw, 0f) * Vector3.forward * Random.Range(Data.ringMin, Data.ringMax);
            }
            // 바닥 위로 붙인다(벽 · 구멍 너머로 떨어지지 않게) — 못 찾으면 그 자리
            if (NavMesh.SamplePosition(p, out var hit, 2f, NavMesh.AllAreas)) p = hit.position;
            _targets[i] = p;

            _discs[i] = PatternGuideHelper.Prepare(
                PatternGuideHelper.Disc(p, Data.landRadius, PatternGuideHelper.Telegraph), ForestGuardianMonster.GuideFlow);
            if (Data.seedPrefab != null)
            {
                _seeds[i] = Object.Instantiate(Data.seedPrefab, _origin, Quaternion.identity);
                _seeds[i].transform.localScale = Vector3.one * Data.seedScale;
                _seedsShownAt = Time.time;
                DissolveEffect.PlayAppear(_seeds[i], SeedAppearSeconds);   // 던지는 손끝에서 맺힌다(10-03)
            }
        }
    }

    // ── 착지 — 피해(한 번) · 이펙트 · 덤불 ──
    private void Land(MonsterContext ctx)
    {
        bool  hitPlayer = false;
        var   player    = ctx.Runtime.CachedPlayer;
        float atk       = ctx.Config?.stat != null ? ctx.Config.stat.attackPower : 0f;
        int   bushDmg   = Mathf.Max(1, (int)(atk * Data.bushDamageMultiplier));

        for (int i = 0; i < _count; i++)
        {
            Vector3 p = _targets[i];
            SpawnOneShot(Data.impactVfxPrefab, p, Data.impactVfxScale);
            if (!hitPlayer && player != null && ctx.Runtime.PlayerTarget != null && atk > 0)
            {
                Vector3 d = ctx.Runtime.PlayerTarget.position - p;
                d.y = 0f;
                if (d.magnitude <= Data.landRadius)
                {
                    hitPlayer = true;
                    player.TakeDamage(Mathf.Max(1, (int)(atk * Data.impactDamageMultiplier)), ctx.Monster.gameObject, false, HitWeight.Auto);
                    Vector3 knock = d.sqrMagnitude > 0.001f ? d.normalized : ctx.Transform.forward;
                    knock.y = 0.3f;
                    player.ApplyKnockback(knock.normalized * ctx.Config.stat.knockbackForce * Data.knockbackMultiplier);
                }
            }
            AddBush(FGThornBush.Spawn(ctx.Monster, p, Data.bushRadius, Data.bushLifetime, Data.bushTickSeconds,
                                      bushDmg, Data.bushColor, Data.bushVfxPrefab, Data.bushVfxScale));
        }
        if (Data.landSfx != null && _count > 0)
            Managers.Sound?.PlayEffectAt(Data.landSfx, _targets[0]);

        ClearFlight();
    }

    /// <summary>동시 덤불 상한 — 넘치면 가장 오래된 것부터 없앤다.</summary>
    private void AddBush(FGThornBush bush)
    {
        for (int i = _bushes.Count - 1; i >= 0; i--)
            if (_bushes[i] == null) _bushes.RemoveAt(i);   // 수명이 다해 사라진 것

        int max = Mathf.Max(1, Data.maxBushes);
        while (_bushes.Count >= max)
        {
            if (_bushes[0] != null) _bushes[0].Vanish();   // 디졸브로 꺼진 뒤 스스로 사라진다(10-03)
            _bushes.RemoveAt(0);
        }
        _bushes.Add(bush);
    }

    private void ClearFlight()
    {
        // 등장 디졸브가 끝난 뒤에만 퇴장 디졸브 — 겹치면 등장 쪽이 끝나며 원본 재질을 되살린다
        bool dissolve = Time.time - _seedsShownAt >= SeedAppearSeconds;
        for (int i = 0; i < _count; i++)
        {
            PatternGuideHelper.SafeDestroy(ref _discs[i]);
            if (_seeds[i] != null) VanishSeed(_seeds[i], dissolve);
            _seeds[i] = null;
        }
    }

    /// <summary>씨앗은 떨어진(또는 끊긴) 자리에서 디졸브로 꺼진 뒤 사라진다(10-03). 퇴장 디졸브는 재질을 되돌리지 않아 끝나자마자 Destroy.</summary>
    private static void VanishSeed(GameObject seed, bool dissolve)
    {
        if (!dissolve) { Object.Destroy(seed); return; }
        DissolveEffect.PlayDisappear(seed, SeedVanishSeconds, () => { if (seed != null) Object.Destroy(seed); });
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
    private static void FacePlayer(MonsterContext ctx, float maxDegrees)
    {
        if (ctx.Runtime.PlayerTarget == null) return;
        Vector3 dir = ctx.Runtime.PlayerTarget.position - ctx.Transform.position;
        dir.y = 0f;
        if (dir.sqrMagnitude < 0.001f) return;
        ctx.Transform.rotation = Quaternion.RotateTowards(ctx.Transform.rotation, Quaternion.LookRotation(dir), maxDegrees);
    }

    private static void PlayAnim(MonsterContext ctx, string stateName)
    {
        if (ctx.Animator == null) return;
        if (!ctx.Animator.HasState(0, Animator.StringToHash(stateName)))
        {
            Debug.LogWarning($"[FGSeedStorm] Animator state not found: '{stateName}'", ctx.Monster);
            return;
        }
        ctx.Animator.speed = 1f;
        ctx.Animator.CrossFade(stateName, 0.1f, 0, 0f);
    }
}
}
