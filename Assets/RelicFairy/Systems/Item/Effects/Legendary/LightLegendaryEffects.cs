using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

// ── L-1. 신성 폭발 (광역기) ───────────────────────────────────────────
public sealed class LightLegendAoeEffect : ItemCombatEffectBase
{
    private static readonly string[] s_hitSfx = { SoundKey.Sfx.MonsterHit1, SoundKey.Sfx.MonsterHit2, SoundKey.Sfx.MonsterHit3 };

    private const int WaveTicks = 4;

    private readonly List<GameObject> _buf = new();
    private float _timer;
    private int _baseTier;
    private bool _baseSet;
    private CancellationTokenSource _cts;

    public LightLegendAoeEffect(ItemEffectSlot s) : base(s) { }

    public override void OnActivate(ItemEffectContext ctx)
    {
        LegendaryRuntime.EnsureExists();
        _timer    = 0f;
        _baseTier = 0;
        _baseSet  = false;
        _cts = new CancellationTokenSource();
    }

    public override void OnDeactivate()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
    }

    public override void OnTick(ItemEffectContext ctx, float deltaTime)
    {
        if (!_baseSet) { _baseTier = MerlinRuneBridge.Instance?.GetZoneTier("LIGHT") ?? 0; _baseSet = true; }
        _timer += deltaTime;
        if (_timer < _value2) return;
        _timer -= _value2;
        if (ctx?.Player == null || _cts == null) return;

        int extra    = ExtraTiers();
        float radius = 3f + extra * 0.4f;

        Vector3 pos = ctx.Player.transform.position;
        var cat = LegendaryRuntime.Catalog;
        float vfxScale = 0.4f + extra * 0.15f;
        LegendaryRuntime.SpawnVfx(cat?.lightAoeVfx, pos, Quaternion.identity, 4f, vfxScale, alpha: 0.18f, yScale: 0.1f);

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

                Vector3 pos = ctx.Player.transform.position;
                int n = CombatQuery.GetNearbyDamageables(pos, radius, Self(ctx), 20, _buf);
                for (int i = 0; i < n; i++)
                {
                    CombatQuery.DealSynergyDamage(_buf[i], EffAtk(ctx) * 0.5f, Self(ctx), element: RuneElement.Light);
                    LegendaryRuntime.SpawnVfx(cat?.lightAoeHitVfx, _buf[i].transform.position, Quaternion.identity, 1.5f, 0.8f);
                }
                if (n > 0)
                    Managers.Sound?.PlayEffectAsync(s_hitSfx[Random.Range(0, s_hitSfx.Length)]).Forget();

                float atkMult = ctx.Player?.RuntimeStats?.AttackSpeedMultiplier ?? 1f;
                await UniTask.Delay(Mathf.RoundToInt(1000f / atkMult), cancellationToken: ct);
            }
        }
        catch (System.OperationCanceledException) { }
    }

    private int ExtraTiers() =>
        Mathf.Max(0, (MerlinRuneBridge.Instance?.GetZoneTier("LIGHT") ?? 0) - _baseTier);
}

// ── L-2. 성스러운 심판 (단일기) ───────────────────────────────────────
public sealed class LightLegendSingleEffect : ItemCombatEffectBase
{
    private static readonly string[] s_hitSfx = { SoundKey.Sfx.MonsterHit1, SoundKey.Sfx.MonsterHit2, SoundKey.Sfx.MonsterHit3 };

    private readonly List<GameObject> _buf = new();
    private int _critCount;
    private int _baseTier;
    private bool _baseSet;
    private const int BaseBeamHits = 5;
    private CancellationTokenSource _cts;

    public LightLegendSingleEffect(ItemEffectSlot s) : base(s) { }

    public override void OnActivate(ItemEffectContext ctx)
    {
        LegendaryRuntime.EnsureExists();
        _critCount = 0;
        _baseTier  = 0;
        _baseSet   = false;
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
        if (!report.IsCrit) return;
        if (!_baseSet) { _baseTier = MerlinRuneBridge.Instance?.GetZoneTier("LIGHT") ?? 0; _baseSet = true; }

        int extra    = ExtraTiers();
        int required = Mathf.Max(1, (int)_value2 - extra * 2);

        if (++_critCount < required) return;
        _critCount = 0;
        if (ctx?.Player == null || _cts == null) return;

        int n = CombatQuery.GetNearbyDamageables(report.HitPosition, 5f, Self(ctx), 1, _buf);
        if (n == 0) return;

        StrikeAsync(ctx, _buf[0], BaseBeamHits, _cts.Token).Forget();
    }

    private async UniTaskVoid StrikeAsync(ItemEffectContext ctx, GameObject target, int count, CancellationToken ct)
    {
        try
        {
            var cat = LegendaryRuntime.Catalog;
            float hitDmg = EffAtk(ctx) * _value;

            for (int i = 0; i < count; i++)
            {
                if (ct.IsCancellationRequested || target == null) break;

                Vector3 tPos = target.transform.position;
                Vector3 spawn = tPos + Vector3.up * 4f;
                // 각도마다 랜덤 Y 회전 → 다양한 방향에서 낙하
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

    private int ExtraTiers() =>
        Mathf.Max(0, (MerlinRuneBridge.Instance?.GetZoneTier("LIGHT") ?? 0) - _baseTier);
}

// ── L-3. 영원의 성검 (투사체) ─────────────────────────────────────────
public sealed class LightLegendProjectileEffect : ItemCombatEffectBase
{
    private int _baseTier;
    private int _lastExtra = -1;
    private bool _baseSet;

    public LightLegendProjectileEffect(ItemEffectSlot s) : base(s) { }

    public override void OnActivate(ItemEffectContext ctx)
    {
        LegendaryRuntime.EnsureExists();
        _baseTier  = 0;
        _lastExtra = -1;
        _baseSet   = false;
    }

    public override void OnDeactivate() => LegendaryRuntime.Instance?.ClearAgentsOf(this);

    public override void OnRoomEnter(ItemEffectContext ctx)
    {
        LegendaryRuntime.Instance?.ClearAgentsOf(this);
        _baseSet = false;
    }

    public override void OnTick(ItemEffectContext ctx, float deltaTime)
    {
        if (!_baseSet)
        {
            _baseTier  = MerlinRuneBridge.Instance?.GetZoneTier("LIGHT") ?? 0;
            _lastExtra = ExtraTiers();
            _baseSet   = true;
            SpawnSwords(ctx);
            return;
        }
        int extra = ExtraTiers();
        if (extra == _lastExtra) return;
        _lastExtra = extra;
        LegendaryRuntime.Instance?.ClearAgentsOf(this);
        SpawnSwords(ctx);
    }

    private void SpawnSwords(ItemEffectContext ctx)
    {
        if (ctx?.Player == null) return;
        int extra = ExtraTiers();
        int count = 5 + extra;
        float dmg = EffAtk(ctx) * _value;

        for (int i = 0; i < count; i++)
        {
            float angle = i * 360f / count + 45f + Random.Range(-10f, 10f);
            Vector3 dir = Quaternion.Euler(0, angle, 0) * Vector3.forward;
            Vector3 pos = LegendaryRuntime.GetRandomNavPoint(LegendaryProjectileBase.FlyHeight);

            var go    = new GameObject("LightSword");
            var agent = go.AddComponent<LightSwordAgent>();
            agent.Init(this, ctx, dmg, 9f, 9999f, RuneElement.Light, dir,
                       maxBounces: 99999, pierce: true);
            go.transform.position = pos;
        }
    }

    private int ExtraTiers() =>
        Mathf.Max(0, (MerlinRuneBridge.Instance?.GetZoneTier("LIGHT") ?? 0) - _baseTier);
}
