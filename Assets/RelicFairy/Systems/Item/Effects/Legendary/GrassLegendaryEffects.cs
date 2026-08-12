using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

// ── P-1. 포자 폭발 (광역기) ───────────────────────────────────────────
public sealed class GrassLegendAoeEffect : ItemCombatEffectBase
{
    private static readonly string[] s_hitSfx = { SoundKey.Sfx.MonsterHit1, SoundKey.Sfx.MonsterHit2, SoundKey.Sfx.MonsterHit3 };

    private const int WaveTicks = 4;

    private readonly List<GameObject> _buf = new();
    private float _timer;
    private int _baseTier;
    private bool _baseSet;
    private CancellationTokenSource _cts;

    public GrassLegendAoeEffect(ItemEffectSlot s) : base(s) { }

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
        if (!_baseSet) { _baseTier = MerlinRuneBridge.Instance?.GetZoneTier("GRASS") ?? 0; _baseSet = true; }
        _timer += deltaTime;
        if (_timer < _value2) return;
        _timer -= _value2;
        if (ctx?.Player == null || _cts == null) return;

        int extra    = ExtraTiers();
        float radius = 3f + extra * 0.4f;

        Vector3 pos = ctx.Player.transform.position;
        var cat = LegendaryRuntime.Catalog;
        float vfxScale = 0.6f + extra * 0.15f;
        LegendaryRuntime.SpawnVfx(cat?.grassAoeVfx, pos, Quaternion.identity, 4f, vfxScale, alpha: 0.18f);

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
                int n = CombatQuery.GetNearbyDamageables(pos, radius, Self(ctx), 30, _buf);
                for (int i = 0; i < n; i++)
                {
                    CombatQuery.DealSynergyDamage(_buf[i], EffAtk(ctx) * 0.5f, Self(ctx), element: RuneElement.Grass);
                    LegendaryRuntime.SpawnVfx(cat?.grassAoeHitVfx, _buf[i].transform.position, Quaternion.identity, 1.5f, 0.8f);
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
        Mathf.Max(0, (MerlinRuneBridge.Instance?.GetZoneTier("GRASS") ?? 0) - _baseTier);
}

// ── P-2. 덩굴 구속 (단일기) ───────────────────────────────────────────
public sealed class GrassLegendSingleEffect : ItemCombatEffectBase
{
    private static readonly string[] s_hitSfx = { SoundKey.Sfx.MonsterHit1, SoundKey.Sfx.MonsterHit2, SoundKey.Sfx.MonsterHit3 };

    private readonly List<GameObject> _buf = new();
    private float _timer;
    private int _baseTier;
    private bool _baseSet;
    private const float BaseBindDuration = 1.5f;
    private const int BindHits = 3;
    private CancellationTokenSource _cts;

    public GrassLegendSingleEffect(ItemEffectSlot s) : base(s) { }

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
        if (!_baseSet) { _baseTier = MerlinRuneBridge.Instance?.GetZoneTier("GRASS") ?? 0; _baseSet = true; }
        _timer += deltaTime;
        if (_timer < _value2) return;
        _timer -= _value2;
        if (ctx?.Player == null || _cts == null) return;

        int n = CombatQuery.GetNearbyDamageables(ctx.Player.transform.position, 20f, Self(ctx), 10, _buf);
        if (n == 0) return;

        BindAsync(ctx, _buf[n - 1], ExtraTiers(), _cts.Token).Forget();
    }

    private async UniTaskVoid BindAsync(ItemEffectContext ctx, GameObject target, int extra, CancellationToken ct)
    {
        try
        {
            var cat = LegendaryRuntime.Catalog;
            float bindDur = BaseBindDuration + extra * 0.3f;
            float hitDmg  = EffAtk(ctx) * _value3 / BindHits;
            int delayMs   = Mathf.RoundToInt(bindDur * 1000f / BindHits);

            Vector3 tPos = target.transform.position;
            Vector3 vfxCenter = target.TryGetComponent<Collider>(out var col)
                ? col.bounds.center : tPos;
            LegendaryRuntime.SpawnVfx(cat?.grassStormVfx, vfxCenter, Quaternion.identity, bindDur + 1f, 0.5f);

            for (int i = 0; i < BindHits; i++)
            {
                if (ct.IsCancellationRequested || target == null) break;

                tPos = target.transform.position;
                CombatQuery.DealSynergyDamage(target, hitDmg, ctx?.Player?.gameObject, element: RuneElement.Grass);
                LegendaryRuntime.SpawnVfx(cat?.grassSingleHitVfx, tPos, Quaternion.identity, 1.5f, 0.8f);
                Managers.Sound?.PlayEffectAsync(s_hitSfx[Random.Range(0, s_hitSfx.Length)]).Forget();

                await UniTask.Delay(delayMs, cancellationToken: ct);
            }
        }
        catch (System.OperationCanceledException) { }
    }

    private int ExtraTiers() =>
        Mathf.Max(0, (MerlinRuneBridge.Instance?.GetZoneTier("GRASS") ?? 0) - _baseTier);
}

// ── P-3. 사방 독 화살 (투사체) ────────────────────────────────────────
public sealed class GrassLegendProjectileEffect : ItemCombatEffectBase
{
    private float _arrowTimer;
    private int _baseTier;
    private bool _baseSet;
    private bool _active;

    private static readonly Vector3[] Dirs =
    {
        Vector3.forward, Vector3.back, Vector3.left, Vector3.right
    };

    private static readonly int MapMask = LayerMask.GetMask("Wall", "Ground");

    public GrassLegendProjectileEffect(ItemEffectSlot s) : base(s) { }

    public override void OnActivate(ItemEffectContext ctx)
    {
        LegendaryRuntime.EnsureExists();
        _baseTier   = 0;
        _baseSet    = false;
        _active     = true;
        _arrowTimer = 0f;
    }

    public override void OnDeactivate()
    {
        _active = false;
        GrassArrowAgent.ClearAll();
    }

    public override void OnRoomEnter(ItemEffectContext ctx)
    {
        GrassArrowAgent.ClearAll();
        _active     = true;
        _arrowTimer = _value2;
    }

    public override void OnTick(ItemEffectContext ctx, float deltaTime)
    {
        if (!_active || ctx?.Player == null) return;
        if (!_baseSet) { _baseTier = MerlinRuneBridge.Instance?.GetZoneTier("GRASS") ?? 0; _baseSet = true; }
        _arrowTimer += deltaTime;
        if (_arrowTimer < _value2) return;
        _arrowTimer = 0f;
        FireArrows(ctx);
    }

    private void FireArrows(ItemEffectContext ctx)
    {
        int extra  = ExtraTiers();
        int perDir = 2 + extra;
        float dmg  = EffAtk(ctx) * _value;

        Vector3 center = ctx.Player.transform.position;
        center.y = LegendaryProjectileBase.FlyHeight;

        foreach (var baseDir in Dirs)
        {
            float spawnDist = 30f;
            if (Physics.Raycast(center, baseDir, out RaycastHit wallHit, 60f, MapMask))
                spawnDist = Mathf.Max(wallHit.distance - 1.5f, 2f);

            for (int i = 0; i < perDir; i++)
            {
                float angleOffset = Random.Range(-40f, 40f);
                float spread = (perDir > 1) ? (i - (perDir - 1) * 0.5f) * 15f : 0f;
                Vector3 fireDir = Quaternion.Euler(0, angleOffset + spread, 0) * (-baseDir);

                var go = new GameObject("GrassArrow");
                go.transform.position = center + baseDir * spawnDist;
                go.AddComponent<GrassArrowAgent>().Init(this, ctx, dmg, 14f, 9999f,
                    RuneElement.Grass, fireDir, maxBounces: 0, pierce: true, hitRadius: 0.4f);
            }
        }
    }

    private int ExtraTiers() =>
        Mathf.Max(0, (MerlinRuneBridge.Instance?.GetZoneTier("GRASS") ?? 0) - _baseTier);
}
