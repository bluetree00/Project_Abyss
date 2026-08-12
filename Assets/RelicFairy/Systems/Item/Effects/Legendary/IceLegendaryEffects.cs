using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.AI;

// ── I-1. 빙하 파동 (광역기) ───────────────────────────────────────────
public sealed class IceLegendAoeEffect : ItemCombatEffectBase
{
    private static readonly string[] s_hitSfx = { SoundKey.Sfx.MonsterHit1, SoundKey.Sfx.MonsterHit2, SoundKey.Sfx.MonsterHit3 };

    private const int WaveTicks = 4;

    private readonly List<GameObject> _buf = new();
    private float _timer;
    private int _baseTier;
    private bool _baseSet;
    private CancellationTokenSource _cts;

    public IceLegendAoeEffect(ItemEffectSlot s) : base(s) { }

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
        if (!_baseSet) { _baseTier = MerlinRuneBridge.Instance?.GetZoneTier("ICE") ?? 0; _baseSet = true; }
        _timer += deltaTime;
        if (_timer < _value2) return;
        _timer -= _value2;
        if (ctx?.Player == null || _cts == null) return;

        int extra = ExtraTiers();
        float radius   = 3f + extra * 0.4f;
        float vfxScale = 0.4f + extra * 0.15f;

        Vector3 pos = ctx.Player.transform.position;
        var cat = LegendaryRuntime.Catalog;
        LegendaryRuntime.SpawnVfx(cat?.iceAoeVfx, pos, Quaternion.identity, 4f, vfxScale, alpha: 0.18f, yScale: 0.1f);

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
                    CombatQuery.DealSynergyDamage(_buf[i], EffAtk(ctx) * 0.5f, Self(ctx), element: RuneElement.Ice);
                    LegendaryRuntime.SpawnVfx(cat?.iceAoeHitVfx, _buf[i].transform.position, Quaternion.identity, 1.5f, 0.8f);
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
        Mathf.Max(0, (MerlinRuneBridge.Instance?.GetZoneTier("ICE") ?? 0) - _baseTier);
}

// ── I-2. 얼음 창 폭격 (단일기) ────────────────────────────────────────
public sealed class IceLegendSingleEffect : ItemCombatEffectBase
{
    private static readonly string[] s_hitSfx = { SoundKey.Sfx.MonsterHit1, SoundKey.Sfx.MonsterHit2, SoundKey.Sfx.MonsterHit3 };

    private readonly List<GameObject> _buf = new();
    private float _timer;
    private int _baseTier;
    private bool _baseSet;
    private const int BaseSpears = 5;
    private CancellationTokenSource _cts;

    public IceLegendSingleEffect(ItemEffectSlot s) : base(s) { }

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
        if (!_baseSet) { _baseTier = MerlinRuneBridge.Instance?.GetZoneTier("ICE") ?? 0; _baseSet = true; }
        _timer += deltaTime;
        if (_timer < _value2) return;
        _timer -= _value2;
        if (ctx?.Player == null || _cts == null) return;

        int n = CombatQuery.GetNearbyDamageables(ctx.Player.transform.position, 20f, Self(ctx), 10, _buf);
        if (n == 0) return;

        StrikeAsync(ctx, _buf[0], BaseSpears + ExtraTiers(), _cts.Token).Forget();
    }

    private async UniTaskVoid StrikeAsync(ItemEffectContext ctx, GameObject target, int count, CancellationToken ct)
    {
        try
        {
            var cat = LegendaryRuntime.Catalog;
            float spearDmg = EffAtk(ctx) * _value;
            float finalDmg = EffAtk(ctx) * _value3;

            for (int i = 0; i < count; i++)
            {
                if (ct.IsCancellationRequested || target == null) break;

                float yaw   = i * 360f / count + Random.Range(-20f, 20f);
                float pitch = Random.Range(-60f, -20f);
                Vector3 tPos   = target.transform.position;
                Vector3 spawn  = tPos + Quaternion.Euler(pitch, yaw, 0) * Vector3.forward * 1.2f;
                Vector3 dir    = tPos - spawn;
                Quaternion rot = dir.sqrMagnitude > 0.01f ? Quaternion.LookRotation(dir) : Quaternion.identity;

                LegendaryRuntime.SpawnVfx(cat?.iceSingleVfx, spawn, rot, 1.0f, 0.1f);
                CombatQuery.DealSynergyDamage(target, spearDmg, ctx?.Player?.gameObject, element: RuneElement.Ice);
                LegendaryRuntime.SpawnVfx(cat?.iceHitVfx, tPos, Quaternion.identity, 1.5f, 0.8f);
                Managers.Sound?.PlayEffectAsync(s_hitSfx[Random.Range(0, s_hitSfx.Length)]).Forget();

                await UniTask.Delay(150, cancellationToken: ct);
            }

            if (target != null)
            {
                CombatQuery.DealSynergyDamage(target, finalDmg, ctx?.Player?.gameObject, element: RuneElement.Ice);
                Managers.Sound?.PlayEffectAsync(s_hitSfx[Random.Range(0, s_hitSfx.Length)]).Forget();
            }
        }
        catch (System.OperationCanceledException) { }
    }

    private int ExtraTiers() =>
        Mathf.Max(0, (MerlinRuneBridge.Instance?.GetZoneTier("ICE") ?? 0) - _baseTier);
}

// ── I-3. 영구 빙판 (필드 지속기) ─────────────────────────────────────
public sealed class IceLegendFieldEffect : ItemCombatEffectBase
{
    private int _baseTier;
    private int _lastExtra = -1;
    private bool _baseSet;
    private ItemEffectContext _savedCtx;

    public IceLegendFieldEffect(ItemEffectSlot s) : base(s) { }

    public override void OnActivate(ItemEffectContext ctx)
    {
        LegendaryRuntime.EnsureExists();
        _baseTier  = 0;
        _lastExtra = -1;
        _baseSet   = false;
        _savedCtx  = ctx;
    }

    public override void OnDeactivate() => LegendaryRuntime.Instance?.ClearAgentsOf(this);

    public override void OnRoomEnter(ItemEffectContext ctx)
    {
        LegendaryRuntime.Instance?.ClearAgentsOf(this);
        _savedCtx = ctx;
        _baseSet  = false;
    }

    public override void OnTick(ItemEffectContext ctx, float deltaTime)
    {
        _savedCtx = ctx;
        if (!_baseSet)
        {
            _baseTier  = MerlinRuneBridge.Instance?.GetZoneTier("ICE") ?? 0;
            _lastExtra = ExtraTiers();
            _baseSet   = true;
            SpawnFields(ctx);
            return;
        }
        int extra = ExtraTiers();
        if (extra == _lastExtra) return;
        _lastExtra = extra;
        LegendaryRuntime.Instance?.ClearAgentsOf(this);
        SpawnFields(ctx);
    }

    private void SpawnFields(ItemEffectContext ctx)
    {
        if (ctx?.Player == null) return;

        int extra = ExtraTiers();
        int count = Mathf.RoundToInt(_value2) + extra;
        float dps = EffAtk(ctx) * _value;
        float atk = EffAtk(ctx);
        float groundY = ctx.Player.transform.position.y;

        var (mapCenter, mapExtents) = CalcNavMeshBounds(groundY);
        float spreadRadius = Mathf.Max(mapExtents.x, mapExtents.z) * 0.55f;

        Debug.Log($"[IceField] SpawnFields start: count={count} value2={_value2} extra={extra} spreadRadius={spreadRadius:F2} center={mapCenter} extents={mapExtents}");

        // 맵을 count 등분 → 각 구역에 1개 확정 스폰
        for (int i = 0; i < count; i++)
        {
            float angle = i * 360f / count + Random.Range(-25f, 25f);
            float rad   = angle * Mathf.Deg2Rad;
            float x     = mapCenter.x + Mathf.Cos(rad) * spreadRadius;
            float z     = mapCenter.z + Mathf.Sin(rad) * spreadRadius;
            var pos = new Vector3(x, groundY, z);

            bool sampled = NavMesh.SamplePosition(pos, out NavMeshHit hit, 5f, NavMesh.AllAreas);
            if (sampled)
                pos = new Vector3(hit.position.x, groundY, hit.position.z);

            Debug.Log($"[IceField] Zone {i}: angle={angle:F1} sampled={sampled} pos={pos}");
            SpawnZone(this, ctx, pos, dps, atk);
            Debug.Log($"[IceField] Zone {i}: spawned OK");
        }

        Debug.Log($"[IceField] SpawnFields end: {count} zones created");
    }

    private static (Vector3 center, Vector3 extents) CalcNavMeshBounds(float y)
    {
        var tri = NavMesh.CalculateTriangulation();
        if (tri.vertices.Length == 0)
            return (new Vector3(0f, y, 0f), new Vector3(6f, 0f, 6f));

        float minX = tri.vertices[0].x, maxX = tri.vertices[0].x;
        float minZ = tri.vertices[0].z, maxZ = tri.vertices[0].z;
        foreach (var v in tri.vertices)
        {
            if (v.x < minX) minX = v.x;
            if (v.x > maxX) maxX = v.x;
            if (v.z < minZ) minZ = v.z;
            if (v.z > maxZ) maxZ = v.z;
        }
        return (new Vector3((minX + maxX) * 0.5f, y, (minZ + maxZ) * 0.5f),
                new Vector3((maxX - minX) * 0.5f, 0f, (maxZ - minZ) * 0.5f));
    }

    private static void SpawnZone(object owner, ItemEffectContext ctx, Vector3 pos, float dps, float atk)
    {
        var go = new GameObject("IceZone");
        go.transform.position = pos;
        var zone = go.AddComponent<IceZoneField>();
        zone.Init(owner, ctx, dps, 1.2f, -1f, atk);
    }

    private int ExtraTiers() =>
        Mathf.Max(0, (MerlinRuneBridge.Instance?.GetZoneTier("ICE") ?? 0) - _baseTier);
}
