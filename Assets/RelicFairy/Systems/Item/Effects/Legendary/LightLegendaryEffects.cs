using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using RelicFairy.Monster;
using UnityEngine;
using UnityEngine.AI;

// ── 빛 전설 3종 — 「몇 초마다」를 없애고 빌드 행동으로 발동(10-14 룬 재설계 R3) ───────────
// 신성 폭발 = 위기 · 성스러운 심판 = 필살 · 영원의 성검 = 사격. 존 단계마다 피해 +15%(개수형은 +1) — LegendaryTuning.

// ── L-1. 신성 폭발 (위기) ─────────────────────────────────────────────
/// <summary>
/// 맞으면(쿨 value2 = 6초) 플레이어 둘레 3.5 m(value3)에 빛 파동 4번(공격력 × value = 50%) + 적을 밀어낸다.
/// 체력 40% 아래에서는 맞지 않아도 6초마다 터진다. 회복은 없다(정책).
/// </summary>
public sealed class LightLegendAoeEffect : ItemCombatEffectBase
{
    private static readonly string[] s_hitSfx = { SoundKey.Sfx.MonsterHit1, SoundKey.Sfx.MonsterHit2, SoundKey.Sfx.MonsterHit3 };
    private const int   WaveTicks = 4;
    private const float LowHp = 0.4f, PushDistance = 1.5f;

    private readonly List<GameObject> _buf = new();
    private float _readyAt;
    private CancellationTokenSource _cts;

    /// <summary>새 행(발동 「AfterHit」)인가 — 옛 CDN 행이면 코드 기본값을 쓴다.</summary>
    private bool NewRow => _trigger == "AfterHit";

    public LightLegendAoeEffect(ItemEffectSlot s) : base(s) { }

    public override void OnActivate(ItemEffectContext ctx)
    {
        LegendaryRuntime.EnsureExists();
        _readyAt = 0f;
        _cts = new CancellationTokenSource();
    }

    public override void OnDeactivate()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
    }

    public override void OnPostTakeDamage(ItemEffectContext ctx, DamageReport report) => TryBurst(ctx);

    public override void OnTick(ItemEffectContext ctx, float deltaTime)
    {
        if (ctx != null && ctx.HpRatio <= LowHp) TryBurst(ctx);
    }

    private void TryBurst(ItemEffectContext ctx)
    {
        if (ctx?.Player == null || _cts == null || Time.time < _readyAt) return;
        float radius = LegendaryTuning.Pick(NewRow, _value3, 3.5f);
        Vector3 pos  = ctx.Player.transform.position;
        if (CombatQuery.GetNearbyDamageables(pos, radius, Self(ctx), 1, _buf) == 0) return;   // 맞을 적이 없으면 아끼다(쿨도 안 돈다)
        _readyAt = Time.time + LegendaryTuning.Pick(NewRow, _value2, 6f);
        var cat = LegendaryRuntime.Catalog;
        LegendaryRuntime.SpawnVfx(cat?.lightAoeVfx, pos, Quaternion.identity, 4f, radius * 0.0377f, alpha: 0.18f, yScale: 0.1f, follow: ctx.Player.transform);
        WaveTickAsync(ctx, radius, _cts.Token).Forget();
    }

    private async UniTaskVoid WaveTickAsync(ItemEffectContext ctx, float radius, CancellationToken ct)
    {
        try
        {
            var cat = LegendaryRuntime.Catalog;
            for (int t = 0; t < WaveTicks; t++)
            {
                if (ct.IsCancellationRequested || ctx.Player == null) break;
                float dmg = LegendaryTuning.Atk(ctx) * LegendaryTuning.Pick(NewRow, _value, 0.5f) * LegendaryTuning.TierMul("LIGHT");
                Vector3 pos = ctx.Player.transform.position;
                int n = CombatQuery.GetNearbyDamageables(pos, radius, Self(ctx), 20, _buf);
                for (int i = 0; i < n; i++)
                {
                    CombatQuery.DealSynergyDamage(_buf[i], dmg, Self(ctx), element: RuneElement.Light);
                    if (t != 0) continue;
                    LegendaryRuntime.SpawnVfx(cat?.lightAoeHitVfx, _buf[i].transform.position, Quaternion.identity, 1.5f, 0.8f);
                    Push(_buf[i], pos);   // 첫 파동이 밀어낸다 — 몰렸을 때 숨 돌릴 자리
                }
                if (n > 0) Managers.Sound?.PlayEffectAsync(s_hitSfx[Random.Range(0, s_hitSfx.Length)]).Forget();
                await UniTask.Delay(400, cancellationToken: ct);
            }
        }
        catch (System.OperationCanceledException) { }
    }

    /// <summary>보스 아닌 적을 플레이어 반대쪽으로 1.5 m — 내비메시 위에서만(벽 너머로 밀지 않는다).</summary>
    private static void Push(GameObject go, Vector3 from)
    {
        var mb = go != null ? go.GetComponentInParent<MonsterBase>() : null;
        if (mb == null || mb.IsDead || mb.Grade == MonsterGrade.Boss) return;
        if (!mb.TryGetComponent<NavMeshAgent>(out var ag) || !ag.isActiveAndEnabled || !ag.isOnNavMesh) return;
        Vector3 away = mb.transform.position - from; away.y = 0f;
        if (away.sqrMagnitude < 0.01f) return;
        Vector3 to = mb.transform.position + away.normalized * PushDistance;
        if (NavMesh.SamplePosition(to, out var hit, 0.6f, ag.areaMask)) ag.Warp(hit.position);
    }
}

// ── L-2. 성스러운 심판 (필살) ─────────────────────────────────────────
/// <summary>
/// 치명 또는 강타 10번마다(value2 · 필살 각인 단계마다 −1 · 최소 5) 마지막 그 대상에게 빛 광선 5번 — 합계 = 그 타격 피해 × value(300%).
/// </summary>
public sealed class LightLegendSingleEffect : ItemCombatEffectBase
{
    private static readonly string[] s_hitSfx = { SoundKey.Sfx.MonsterHit1, SoundKey.Sfx.MonsterHit2, SoundKey.Sfx.MonsterHit3 };
    private const int Beams = 5, MinRequired = 5;

    private int _count;
    private CancellationTokenSource _cts;

    /// <summary>새 행(발동 「OnHeavyHit」)인가 — 옛 CDN 행(OnCritCount · 공격력 150% × 5 · 15회)이면 코드 기본값을 쓴다.</summary>
    private bool NewRow => _trigger == "OnHeavyHit";

    public LightLegendSingleEffect(ItemEffectSlot s) : base(s) { }

    public override void OnActivate(ItemEffectContext ctx)
    {
        LegendaryRuntime.EnsureExists();
        _count = 0;
        _cts = new CancellationTokenSource();
    }

    public override void OnDeactivate()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
    }

    public override void OnPostDealDamage(ItemEffectContext ctx, DamageReport report)
    {
        if (!(report.IsCrit || report.IsHeavy) || report.DamageDealt <= 0f || report.Target == null) return;
        int required = Mathf.Max(MinRequired, Mathf.RoundToInt(LegendaryTuning.Pick(NewRow, _value2, 10f)) - BuildImprint.Stage(BuildFamily.Crit));
        if (++_count < required) return;
        _count = 0;
        if (ctx?.Player == null || _cts == null) return;
        float total = report.DamageDealt * LegendaryTuning.Pick(NewRow, _value, 3f) * LegendaryTuning.TierMul("LIGHT");
        StrikeAsync(ctx, report.Target, total / Beams, _cts.Token).Forget();
    }

    private async UniTaskVoid StrikeAsync(ItemEffectContext ctx, GameObject target, float hitDmg, CancellationToken ct)
    {
        try
        {
            var cat = LegendaryRuntime.Catalog;
            for (int i = 0; i < Beams; i++)
            {
                if (ct.IsCancellationRequested || target == null) break;

                Vector3 tPos = target.transform.position;
                Vector3 spawn = tPos + Vector3.up * 4f;
                Quaternion strikeRot = Quaternion.Euler(-90f, Random.Range(0f, 360f), 0f);

                // PurifierBeam 내부의 ParticleBeam만 추출하여 사용
                if (cat?.lightBeamVfx != null)
                {
                    var parent = Object.Instantiate(cat.lightBeamVfx, Vector3.zero, Quaternion.identity);
                    var beamTf = parent.transform.Find("Effect_28_ParticleBeam");
                    if (beamTf != null)
                    {
                        beamTf.SetParent(null);
                        beamTf.SetPositionAndRotation(spawn, strikeRot);
                        beamTf.localScale = new Vector3(0.25f, 3f, 0.25f);
                        Object.Destroy(beamTf.gameObject, 1.0f);
                    }
                    Object.Destroy(parent);
                }

                CombatQuery.DealSynergyDamage(target, hitDmg, ctx?.Player?.gameObject, element: RuneElement.Light);
                LegendaryRuntime.SpawnVfx(cat?.lightSingleHitVfx, tPos, Quaternion.identity, 1.5f, 0.8f);
                Managers.Sound?.PlayEffectAsync(s_hitSfx[Random.Range(0, s_hitSfx.Length)]).Forget();

                await UniTask.Delay(150, cancellationToken: ct);
            }
        }
        catch (System.OperationCanceledException) { }
    }
}

// ── L-3. 영원의 성검 (사격) ───────────────────────────────────────────
/// <summary>
/// 방에 들어서면 성검 2자루(존 단계마다 +1) — 원거리 명중 8번마다(value2) +1(최대 6). 튕겨 다니며 닿은 적에게 공격력 × value(60%) · 같은 대상 0.4초에 한 번.
/// </summary>
public sealed class LightLegendProjectileEffect : ItemCombatEffectBase
{
    private const int   StartSwords = 2, MaxSwords = 6;
    private const float HitGap = 0.4f;

    private int _swords, _hits;
    private bool _spawned;
    private ItemEffectContext _ctx;

    /// <summary>새 행(발동 「OnRangedHit」)인가 — 옛 CDN 행이면 코드 기본값을 쓴다.</summary>
    private bool NewRow => _trigger == "OnRangedHit";

    public LightLegendProjectileEffect(ItemEffectSlot s) : base(s) { }

    public override void OnActivate(ItemEffectContext ctx)
    {
        LegendaryRuntime.EnsureExists();
        _ctx = ctx;
        _spawned = false;
    }

    public override void OnDeactivate() => LegendaryRuntime.Instance?.ClearAgentsOf(this);

    public override void OnRoomEnter(ItemEffectContext ctx)
    {
        LegendaryRuntime.Instance?.ClearAgentsOf(this);
        _spawned = false;
    }

    public override void OnTick(ItemEffectContext ctx, float deltaTime)
    {
        _ctx = ctx;
        if (_spawned || ctx?.Player == null) return;
        _spawned = true;
        _hits = 0;
        _swords = 0;
        int start = Mathf.Min(MaxSwords + LegendaryTuning.Tier("LIGHT"), StartSwords + LegendaryTuning.Tier("LIGHT"));
        for (int i = 0; i < start; i++) SpawnSword(ctx, i * 360f / start);
    }

    public override void OnPostDealDamage(ItemEffectContext ctx, DamageReport report)
    {
        if (!report.IsRanged || report.DamageDealt <= 0f || ctx?.Player == null) return;
        int required = Mathf.Max(1, Mathf.RoundToInt(LegendaryTuning.Pick(NewRow, _value2, 8f)));
        if (++_hits < required) return;
        _hits = 0;
        if (_swords < MaxSwords + LegendaryTuning.Tier("LIGHT")) SpawnSword(ctx, Random.Range(0f, 360f));
    }

    // 개수형 — 존 단계는 자루 수(+1)로 받는다(피해 배율은 붙이지 않는다)
    private float SwordDamage() => LegendaryTuning.Atk(_ctx) * LegendaryTuning.Pick(NewRow, _value, 0.6f);

    private void SpawnSword(ItemEffectContext ctx, float angle)
    {
        Vector3 dir = Quaternion.Euler(0, angle + 45f, 0) * Vector3.forward;
        Vector3 pos = LegendaryRuntime.GetRandomNavPoint(LegendaryProjectileBase.FlyHeight);
        var go    = new GameObject("LightSword");
        var agent = go.AddComponent<LightSwordAgent>();
        agent.Init(this, ctx, 0f, 9f, 9999f, RuneElement.Light, dir, maxBounces: 99999, pierce: true);
        agent.PerTargetGap = HitGap;
        agent.DamageFn     = SwordDamage;
        go.transform.position = pos;
        _swords++;
    }
}
