using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using RelicFairy.Monster;
using UnityEngine;

// ── 숲 전설 3종 — 「몇 초마다」를 없애고 빌드 행동으로 발동(10-14 룬 재설계 R3) ───────────
// 포자 폭발 = 연격 · 덩굴 구속 = 사격 · 사방 독 화살 = 사격. 존 단계마다 피해 +15% — LegendaryTuning.

// ── P-1. 포자 폭발 (연격) ─────────────────────────────────────────────
/// <summary>
/// 맞힐 때마다 독 1중첩(독 룬과 같은 「item_poison」 · 최대 8 · 중첩당 초당 공격력 × value2 = 4%) —
/// 8중첩이 된 적 자리에서 포자가 터진다: 반경 3 m(value3) 4번 · 공격력 × value(40%). 같은 적은 3초에 한 번.
/// </summary>
public sealed class GrassLegendAoeEffect : ItemCombatEffectBase
{
    private static readonly string[] s_hitSfx = { SoundKey.Sfx.MonsterHit1, SoundKey.Sfx.MonsterHit2, SoundKey.Sfx.MonsterHit3 };
    private const int   WaveTicks = 4, MaxStacks = 8, PoisonTicks = 4;
    private const string PoisonId = "item_poison", BurstCd = "spore_burst";
    private const float BurstGap = 3f;

    private readonly List<GameObject> _buf = new();
    private CancellationTokenSource _cts;

    /// <summary>새 행(발동 「OnHit」)인가 — 옛 CDN 행이면 코드 기본값을 쓴다.</summary>
    private bool NewRow => _trigger == "OnHit";

    public GrassLegendAoeEffect(ItemEffectSlot s) : base(s) { }

    public override void OnActivate(ItemEffectContext ctx)
    {
        LegendaryRuntime.EnsureExists();
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
        if (report.DamageDealt <= 0f || report.Target == null || ctx?.Player == null || _cts == null) return;
        var mb = report.Target.GetComponentInParent<MonsterBase>();
        if (mb == null || mb.IsDead) return;

        float perStack = LegendaryTuning.Atk(ctx) * LegendaryTuning.Pick(NewRow, _value2, 0.04f);
        mb.Status.ApplyStackingDot(PoisonId, Mathf.Max(1f, perStack), 1f, PoisonTicks, MaxStacks, Self(ctx));
        if (mb.Status.GetDotStacks(PoisonId) < MaxStacks || mb.Status.IsOnCooldown(BurstCd)) return;
        mb.Status.SetCooldown(BurstCd, BurstGap);
        BurstAsync(ctx, mb.transform.position, _cts.Token).Forget();
    }

    private async UniTaskVoid BurstAsync(ItemEffectContext ctx, Vector3 at, CancellationToken ct)
    {
        try
        {
            var cat = LegendaryRuntime.Catalog;
            float radius = LegendaryTuning.Pick(NewRow, _value3, 3f);
            LegendaryRuntime.SpawnVfx(cat?.grassAoeVfx, at, Quaternion.identity, 4f, radius * 0.0556f, alpha: 0.18f);
            SkillRangeRing.Circle(at, radius);
            for (int t = 0; t < WaveTicks; t++)
            {
                if (ct.IsCancellationRequested || ctx.Player == null) break;
                float dmg = LegendaryTuning.Atk(ctx) * LegendaryTuning.Pick(NewRow, _value, 0.4f) * LegendaryTuning.TierMul("GRASS");
                int n = CombatQuery.GetNearbyDamageables(at, radius, Self(ctx), 30, _buf);
                for (int i = 0; i < n; i++)
                {
                    CombatQuery.DealSynergyDamage(_buf[i], dmg, Self(ctx), element: RuneElement.Grass);
                    if (t == 0) LegendaryRuntime.SpawnVfx(cat?.grassAoeHitVfx, _buf[i].transform.position, Quaternion.identity, 1.5f, 0.8f);
                }
                if (n > 0) Managers.Sound?.PlayEffectAsync(s_hitSfx[Random.Range(0, s_hitSfx.Length)]).Forget();
                await UniTask.Delay(400, cancellationToken: ct);
            }
        }
        catch (System.OperationCanceledException) { }
    }
}

// ── P-2. 덩굴 구속 (사격) ─────────────────────────────────────────────
/// <summary>
/// 원거리 명중 10번마다(value2) 그 대상을 덩굴이 1.5초 속박(보스 제외) — 3번에 걸쳐 합계 공격력 × value3(240%).
/// </summary>
public sealed class GrassLegendSingleEffect : ItemCombatEffectBase
{
    private static readonly string[] s_hitSfx = { SoundKey.Sfx.MonsterHit1, SoundKey.Sfx.MonsterHit2, SoundKey.Sfx.MonsterHit3 };
    private const float BindSeconds = 1.5f;
    private const int   BindHits = 3;

    private int _hits;
    private CancellationTokenSource _cts;

    /// <summary>새 행(발동 「OnRangedHit」)인가 — 옛 CDN 행이면 코드 기본값을 쓴다.</summary>
    private bool NewRow => _trigger == "OnRangedHit";

    public GrassLegendSingleEffect(ItemEffectSlot s) : base(s) { }

    public override void OnActivate(ItemEffectContext ctx)
    {
        LegendaryRuntime.EnsureExists();
        _hits = 0;
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
        if (!report.IsRanged || report.DamageDealt <= 0f || report.Target == null || ctx?.Player == null || _cts == null) return;
        int required = Mathf.Max(1, Mathf.RoundToInt(LegendaryTuning.Pick(NewRow, _value2, 10f)));
        if (++_hits < required) return;
        _hits = 0;
        BindAsync(ctx, report.Target, _cts.Token).Forget();
    }

    private async UniTaskVoid BindAsync(ItemEffectContext ctx, GameObject target, CancellationToken ct)
    {
        try
        {
            var cat = LegendaryRuntime.Catalog;
            var mb = target.GetComponentInParent<MonsterBase>();
            if (mb != null && mb.Grade != MonsterGrade.Boss) mb.Status.ApplyCc("bind", BindSeconds);   // 이름대로 실제 구속
            float hitDmg = LegendaryTuning.Atk(ctx) * LegendaryTuning.Pick(NewRow, _value3, 2.4f) * LegendaryTuning.TierMul("GRASS") / BindHits;
            int delayMs  = Mathf.RoundToInt(BindSeconds * 1000f / BindHits);

            Vector3 vfxCenter = target.TryGetComponent<Collider>(out var col) ? col.bounds.center : target.transform.position;
            LegendaryRuntime.SpawnVfx(cat?.grassStormVfx, vfxCenter, Quaternion.identity, BindSeconds + 1f, 0.5f);

            for (int i = 0; i < BindHits; i++)
            {
                if (ct.IsCancellationRequested || target == null) break;
                CombatQuery.DealSynergyDamage(target, hitDmg, ctx?.Player?.gameObject, element: RuneElement.Grass);
                LegendaryRuntime.SpawnVfx(cat?.grassSingleHitVfx, target.transform.position, Quaternion.identity, 1.5f, 0.8f);
                Managers.Sound?.PlayEffectAsync(s_hitSfx[Random.Range(0, s_hitSfx.Length)]).Forget();
                await UniTask.Delay(delayMs, cancellationToken: ct);
            }
        }
        catch (System.OperationCanceledException) { }
    }
}

// ── P-3. 사방 독 화살 (사격) ──────────────────────────────────────────
/// <summary>
/// 원거리 명중 6번마다(value2) 그 대상 둘레에서 독 화살 8발이 대상 쪽으로 모인다 — 한 발 공격력 × value(25%) · 같은 대상 0.3초에 한 번.
/// </summary>
public sealed class GrassLegendProjectileEffect : ItemCombatEffectBase
{
    private const int   Arrows = 8;
    private const float SpawnRadius = 6f, ArrowSpeed = 14f, ArrowLife = 1.2f, HitGap = 0.3f;

    private int _hits;
    private ItemEffectContext _ctx;

    /// <summary>새 행(발동 「OnRangedHit」 · value2 6)인가 — 옛 CDN 행(OnActivate)이면 코드 기본값을 쓴다.</summary>
    private bool NewRow => _trigger == "OnRangedHit";

    public GrassLegendProjectileEffect(ItemEffectSlot s) : base(s) { }

    public override void OnActivate(ItemEffectContext ctx)
    {
        LegendaryRuntime.EnsureExists();
        _ctx = ctx;
        _hits = 0;
    }

    public override void OnDeactivate() => GrassArrowAgent.ClearAll();

    public override void OnRoomEnter(ItemEffectContext ctx) => GrassArrowAgent.ClearAll();

    public override void OnPostDealDamage(ItemEffectContext ctx, DamageReport report)
    {
        if (!report.IsRanged || report.DamageDealt <= 0f || report.Target == null || ctx?.Player == null) return;
        _ctx = ctx;
        int required = Mathf.Max(1, Mathf.RoundToInt(LegendaryTuning.Pick(NewRow, _value2, 6f)));
        if (++_hits < required) return;
        _hits = 0;
        Converge(ctx, report.Target.transform.position);
    }

    private float ArrowDamage()
        => LegendaryTuning.Atk(_ctx) * LegendaryTuning.Pick(NewRow, _value, 0.25f) * LegendaryTuning.TierMul("GRASS");

    private void Converge(ItemEffectContext ctx, Vector3 at)
    {
        Vector3 center = new Vector3(at.x, LegendaryProjectileBase.FlyHeight + ctx.Player.transform.position.y, at.z);
        float start = Random.Range(0f, 360f);
        for (int i = 0; i < Arrows; i++)
        {
            Vector3 outDir = Quaternion.Euler(0f, start + i * 360f / Arrows, 0f) * Vector3.forward;
            var go = new GameObject("GrassArrow");
            go.transform.position = center + outDir * SpawnRadius;
            var arrow = go.AddComponent<GrassArrowAgent>();
            arrow.Init(this, ctx, 0f, ArrowSpeed, ArrowLife, RuneElement.Grass, -outDir, maxBounces: 0, pierce: true, hitRadius: 0.4f);
            arrow.PerTargetGap = HitGap;
            arrow.DamageFn     = ArrowDamage;
        }
    }
}
