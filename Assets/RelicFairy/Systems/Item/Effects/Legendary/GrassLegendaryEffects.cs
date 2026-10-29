using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using RelicFairy.Monster;
using UnityEngine;

// ── 숲 전설 3종 — 「몇 초마다」를 없애고 빌드 행동으로 발동(10-14 룬 재설계 R3) ───────────
// 포자 폭발 = 연격 · 덩굴 구속 = 사격 · 사방 독 화살 = 사격. 존 단계마다 피해 +15% — LegendaryTuning.

// ── P-1. 포자 전염 (연격 · 옛 포자 폭발) ─────────────────────────────────
/// <summary>
/// 10-24 개편 — 적중마다 독 1중첩(item_poison · 최대 8 · 중첩당 초당 공격력 × value2 = 4%). 8중첩 적은 포자를 터뜨려
/// 반경 value3(3 m) 적에게 독 4중첩을 옮기고 4번 공격력 × value(40%). 독 걸린 채 쓰러진 적도 터진다. 8중첩 적은 받는 피해 +12%.
/// 존 단계마다 +20% · 각성(풀 존 4단계) = 옮기는 중첩 8(옮은 적이 바로 다음 터짐 후보).
/// </summary>
public sealed class GrassLegendAoeEffect : ItemCombatEffectBase
{
    private static readonly string[] s_hitSfx = { SoundKey.Sfx.MonsterHit1, SoundKey.Sfx.MonsterHit2, SoundKey.Sfx.MonsterHit3 };
    // 장판 그림 수명 — 피해는 0.4초 × 4틱(1.2초에 끝)인데 그림이 4초 남아 피해 없는 장판으로 읽혔다(10-18 이펙트 점검)
    private const float AoeVfxLife = 1.8f;
    private const int   WaveTicks = 4, MaxStacks = 8, PoisonTicks = 4, SpreadStacks = 4;
    private const string PoisonId = "item_poison", BurstCd = "spore_burst";
    private const float BurstGap = 3f, FullAmp = 0.12f, FullAmpSeconds = 1.5f;

    private readonly List<GameObject> _buf = new();
    private readonly List<MonsterBase> _near = new();
    private CancellationTokenSource _cts;

    /// <summary>새 행(발동 「OnHitSpread」)인가 — 옛 CDN 행이면 코드 기본값을 쓴다.</summary>
    private bool NewRow => _trigger == "OnHitSpread";

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

    private float PerStack(ItemEffectContext ctx) => Mathf.Max(1f, LegendaryTuning.Atk(ctx) * LegendaryTuning.Pick(NewRow, _value2, 0.04f));

    public override void OnPostDealDamage(ItemEffectContext ctx, DamageReport report)
    {
        if (report.DamageDealt <= 0f || report.Target == null || ctx?.Player == null || _cts == null) return;
        var mb = report.Target.GetComponentInParent<MonsterBase>();
        if (mb == null || mb.IsDead) return;

        mb.Status.ApplyStackingDot(PoisonId, PerStack(ctx), 1f, PoisonTicks, MaxStacks, Self(ctx));
        if (mb.Status.GetDotStacks(PoisonId) < MaxStacks) return;
        mb.ApplyDamageTakenAmp(FullAmp, FullAmpSeconds, "spore");   // 8중첩 적 — 받는 피해 +12%(맞을 때마다 갱신)
        if (mb.Status.IsOnCooldown(BurstCd)) return;
        mb.Status.SetCooldown(BurstCd, BurstGap);
        Burst(ctx, mb);
    }

    public override void OnKill(ItemEffectContext ctx, GameObject target)
    {
        var mb = target != null ? target.GetComponentInParent<MonsterBase>() : null;
        if (mb == null || ctx?.Player == null || _cts == null || mb.Status.GetDotStacks(PoisonId) <= 0) return;   // 독 걸린 채 쓰러졌다 — 터진다
        Burst(ctx, mb);
    }

    private void Burst(ItemEffectContext ctx, MonsterBase src)
    {
        Vector3 at = src.transform.position;
        float radius = LegendaryTuning.Pick(NewRow, _value3, 3f);
        int spread = LegendaryTuning.Awakened("GRASS") ? MaxStacks : SpreadStacks;
        float perStack = PerStack(ctx);
        int n = CombatQuery.GetNearbyEnemies(at, radius, Self(ctx), 16, _near);
        for (int i = 0; i < n; i++)
        {
            var m = _near[i];
            if (m == null || m == src || m.IsDead) continue;
            for (int k = 0; k < spread; k++) m.Status.ApplyStackingDot(PoisonId, perStack, 1f, PoisonTicks, MaxStacks, Self(ctx));   // 포자가 옮는다
        }
        BurstAsync(ctx, at, radius, _cts.Token).Forget();
    }

    private async UniTaskVoid BurstAsync(ItemEffectContext ctx, Vector3 at, float radius, CancellationToken ct)
    {
        try
        {
            var cat = LegendaryRuntime.Catalog;
            LegendaryRuntime.SpawnVfx(cat?.grassAoeVfx, at, Quaternion.identity, AoeVfxLife, radius * 0.0556f, alpha: 0.18f);
            SkillRangeRing.Circle(at, radius);
            for (int t = 0; t < WaveTicks; t++)
            {
                if (ct.IsCancellationRequested || ctx.Player == null) break;
                float dmg = LegendaryTuning.Atk(ctx) * LegendaryTuning.Pick(NewRow, _value, 0.4f) * LegendaryTuning.TierMul("GRASS");
                int n = CombatQuery.GetNearbyDamageables(at, radius, Self(ctx), 30, _buf);
                for (int i = 0; i < n; i++)
                {
                    CombatQuery.DealSynergyDamage(_buf[i], dmg, Self(ctx), element: RuneElement.Grass);
                    if (t == 0) LegendaryRuntime.SpawnVfx(cat?.grassAoeHitVfx, _buf[i].transform.position, Quaternion.identity, 1.5f, 0.52f);   // 대상 타격 ≈ 1 m(10-22 B2 실측 83: ×0.8 = 1.5 m)
                }
                if (n > 0) Managers.Sound?.PlayEffectAsync(s_hitSfx[Random.Range(0, s_hitSfx.Length)]).Forget();
                await UniTask.Delay(400, cancellationToken: ct);
            }
        }
        catch (System.OperationCanceledException) { }
    }
}

// ── P-2. 뿌리 표적 (사격 · 옛 덩굴 구속) ─────────────────────────────────
/// <summary>
/// 10-24 개편 — 원거리 명중 value2(8)번마다 그 적에 덩굴 표적(속박 1.5초 · 표적 value3 = 4초 · 보스는 속박 없이 표적만).
/// 표적에 맞는 화살 = 독 2중첩 + 표적 둘레 3 m 적에게 그 화살 피해의 value(35%) + 5%p × 존 단계가 튄다.
/// 각성(풀 존 4단계) = 표적이 쓰러지면 6 m 안 가장 가까운 적으로 옮겨 간다.
/// </summary>
public sealed class GrassLegendSingleEffect : ItemCombatEffectBase
{
    private const float BindSeconds = 1.5f, SplashRadius = 3f, MoveRadius = 6f, SplashPerTier = 0.05f, LineGap = 0.2f;
    private const int   PoisonPerArrow = 2, MaxStacks = 8, PoisonTicks = 4;
    private const string PoisonId = "item_poison";

    private readonly List<MonsterBase> _near = new();
    private MonsterBase _mark;
    private float _markUntil, _lineReadyAt;
    private int _hits;

    /// <summary>새 행(발동 「OnRangedMark」)인가 — 옛 CDN 행이면 코드 기본값을 쓴다.</summary>
    private bool NewRow => _trigger == "OnRangedMark";

    public GrassLegendSingleEffect(ItemEffectSlot s) : base(s) { }

    public override void OnActivate(ItemEffectContext ctx)
    {
        LegendaryRuntime.EnsureExists();
        _hits = 0;
        _mark = null;
    }

    public override void OnDeactivate() => _mark = null;

    public override void OnPostDealDamage(ItemEffectContext ctx, DamageReport report)
    {
        if (!report.IsRanged || report.DamageDealt <= 0f || report.Target == null || ctx?.Player == null) return;
        var mb = report.Target.GetComponentInParent<MonsterBase>();
        if (_mark != null && mb == _mark && Time.time < _markUntil) Splash(ctx, mb, report.DamageDealt);
        int required = Mathf.Max(1, Mathf.RoundToInt(LegendaryTuning.Pick(NewRow, _value2, 8f)));
        if (++_hits < required) return;
        _hits = 0;
        if (mb != null && !mb.IsDead) Mark(mb);
    }

    public override void OnKill(ItemEffectContext ctx, GameObject target)
    {
        if (_mark == null || target == null || target.GetComponentInParent<MonsterBase>() != _mark) return;
        var dead = _mark;
        _mark = null;
        if (!LegendaryTuning.Awakened("GRASS") || ctx?.Player == null) return;   // 각성 — 표적이 옮겨 간다
        int n = CombatQuery.GetNearbyEnemies(dead.transform.position, MoveRadius, Self(ctx), 16, _near);
        MonsterBase next = null;
        float best = float.MaxValue;
        for (int i = 0; i < n; i++)
        {
            var m = _near[i];
            if (m == null || m == dead || m.IsDead) continue;
            float d = (m.transform.position - dead.transform.position).sqrMagnitude;
            if (d < best) { best = d; next = m; }
        }
        if (next != null) Mark(next);
    }

    private void Mark(MonsterBase mb)
    {
        float seconds = LegendaryTuning.Pick(NewRow, _value3, 4f);
        _mark = mb;
        _markUntil = Time.time + seconds;
        if (mb.Grade != MonsterGrade.Boss) mb.Status.ApplyCc("bind", BindSeconds);   // 이름대로 실제 구속
        var cat = LegendaryRuntime.Catalog;
        Vector3 vfxCenter = mb.TryGetComponent<Collider>(out var col) ? col.bounds.center : mb.transform.position;
        LegendaryRuntime.SpawnVfx(cat?.grassStormVfx, vfxCenter, Quaternion.identity, BindSeconds + 1f, 0.5f);
        ElementVfxPlayer.AttachStatus(RuneElement.Grass, mb.transform, seconds);   // 표적 동안 몸 표시
    }

    private void Splash(ItemEffectContext ctx, MonsterBase mb, float arrow)
    {
        float perStack = Mathf.Max(1f, LegendaryTuning.Atk(ctx) * 0.04f);
        for (int k = 0; k < PoisonPerArrow; k++) mb.Status.ApplyStackingDot(PoisonId, perStack, 1f, PoisonTicks, MaxStacks, Self(ctx));
        float splash = arrow * (LegendaryTuning.Pick(NewRow, _value, 0.35f) + SplashPerTier * LegendaryTuning.Tier("GRASS"));
        bool line = Time.time >= _lineReadyAt;
        if (line) _lineReadyAt = Time.time + LineGap;
        int n = CombatQuery.GetNearbyEnemies(mb.transform.position, SplashRadius, Self(ctx), 16, _near);
        for (int i = 0; i < n; i++)
        {
            var m = _near[i];
            if (m == null || m == mb || m.IsDead) continue;
            CombatQuery.DealSynergyDamage(m, splash, Self(ctx), element: RuneElement.Grass);
            if (line) ElementVfxPlayer.PlayBeam(RuneElement.Grass, mb.transform.position + Vector3.up * 0.8f, m.transform.position + Vector3.up * 0.8f, 0.15f);
        }
    }
}

// ── P-3. 독안개 사수 (사격 · 옛 사방 독 화살) ─────────────────────────────
/// <summary>
/// 10-24 개편 — 원거리 명중 value2(6)번마다 맞은 자리에 독안개(반경 value3 = 2.5 m · 5초 · 최대 2).
/// 안개 안의 적을 맞힌 원거리 = 독 화살: 그 타격의 value(35%)만큼 더(존 배율) + 독 2중첩(item_poison).
/// 각성(풀 존 4단계) = 안개 최대 3 · 안개 안 적 받는 피해 +10%. 설계의 「관통 +1」은 화살 체계를 건드려 뺐다.
/// </summary>
public sealed class GrassLegendProjectileEffect : ItemCombatEffectBase
{
    private const float MistSeconds = 5f, AwakeAmp = 0.1f, AmpSeconds = 0.4f, PoisonPerStack = 0.04f;
    private const int   MaxMists = 2, AwakeMaxMists = 3, PoisonPerArrow = 2, PoisonTicks = 4, MaxStacks = 8;
    private const string PoisonId = "item_poison";

    private readonly List<LegendGroundField> _mists = new();
    private int _hits;

    /// <summary>새 행(발동 「OnRangedMist」)인가 — 옛 CDN 행이면 코드 기본값을 쓴다.</summary>
    private bool NewRow => _trigger == "OnRangedMist";

    public GrassLegendProjectileEffect(ItemEffectSlot s) : base(s) { }

    public override void OnActivate(ItemEffectContext ctx)
    {
        LegendaryRuntime.EnsureExists();
        _hits = 0;
    }

    public override void OnDeactivate() => Clear();

    public override void OnRoomEnter(ItemEffectContext ctx) => Clear();

    public override void OnPostDealDamage(ItemEffectContext ctx, DamageReport report)
    {
        if (!report.IsRanged || report.DamageDealt <= 0f || report.Target == null || ctx?.Player == null) return;
        Vector3 at = report.Target.transform.position;
        _mists.RemoveAll(m => m == null);
        if (InMist(at)) PoisonArrow(ctx, report);
        int required = Mathf.Max(1, Mathf.RoundToInt(LegendaryTuning.Pick(NewRow, _value2, 6f)));
        if (++_hits < required) return;
        _hits = 0;
        SpawnMist(ctx, at);
    }

    private bool InMist(Vector3 p)
    {
        foreach (var m in _mists) if (m != null && m.Contains(p)) return true;
        return false;
    }

    private void PoisonArrow(ItemEffectContext ctx, DamageReport report)
    {
        float extra = report.DamageDealt * LegendaryTuning.Pick(NewRow, _value, 0.35f) * LegendaryTuning.TierMul("GRASS");
        CombatQuery.DealSynergyDamage(report.Target, extra, Self(ctx), element: RuneElement.Grass);
        var mb = report.Target.GetComponentInParent<MonsterBase>();
        if (mb == null || mb.IsDead) return;
        float perStack = Mathf.Max(1f, LegendaryTuning.Atk(ctx) * PoisonPerStack);
        for (int k = 0; k < PoisonPerArrow; k++) mb.Status.ApplyStackingDot(PoisonId, perStack, 1f, PoisonTicks, MaxStacks, Self(ctx));
    }

    private void SpawnMist(ItemEffectContext ctx, Vector3 at)
    {
        bool awake = LegendaryTuning.Awakened("GRASS");
        int max = awake ? AwakeMaxMists : MaxMists;
        while (_mists.Count >= max) { if (_mists[0] != null) Object.Destroy(_mists[0].gameObject); _mists.RemoveAt(0); }
        float radius = LegendaryTuning.Pick(NewRow, _value3, 2.5f);
        System.Action<MonsterBase> onTick = null;
        if (awake) onTick = mb => mb.ApplyDamageTakenAmp(AwakeAmp, AmpSeconds, "poison_mist");   // 각성 — 안개 안 적 받는 피해
        _mists.Add(LegendGroundField.Spawn(this, ctx, at, at, radius, MistSeconds, RuneElement.Grass, onTick));
    }

    private void Clear()
    {
        LegendaryRuntime.Instance?.ClearAgentsOf(this);
        _mists.Clear();
    }
}
