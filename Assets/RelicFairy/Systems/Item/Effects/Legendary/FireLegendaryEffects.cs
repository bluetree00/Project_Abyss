using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

// ── 공통 헬퍼 ─────────────────────────────────────────────────────────
// 배치 시점의 FIRE 존 시너지 단계를 저장하고, 추후 추가된 단계 수를 추가 수량으로 환산한다.
//   _baseTier  : OnActivate 시점 GetZoneTier("FIRE")
//   ExtraTiers : 이후 비레전드리 룬으로 올라간 단계 수

// ── F-1. 마그마 분출 (광역기) ─────────────────────────────────────────
public sealed class FireLegendAoeEffect : ItemCombatEffectBase
{
    private static readonly string[] s_hitSfx = { SoundKey.Sfx.MonsterHit1, SoundKey.Sfx.MonsterHit2, SoundKey.Sfx.MonsterHit3 };

    private readonly List<GameObject> _buf = new();
    private float _timer;
    private int _baseTier;
    private bool _baseSet;

    public FireLegendAoeEffect(ItemEffectSlot s) : base(s) { }

    public override void OnActivate(ItemEffectContext ctx)
    {
        LegendaryRuntime.EnsureExists();
        _timer    = 0f;
        _baseTier = 0;
        _baseSet  = false;
    }

    public override void OnTick(ItemEffectContext ctx, float deltaTime)
    {
        if (!_baseSet) { _baseTier = MerlinRuneBridge.Instance?.GetZoneTier("FIRE") ?? 0; _baseSet = true; }
        _timer += deltaTime;
        if (_timer < _value2) return;
        _timer -= _value2;
        if (ctx?.Player == null) return;

        int extra = ExtraTiers();
        float radius = 3f + extra * 0.4f;

        Vector3 pos = ctx.Player.transform.position;
        float dmg  = EffAtk(ctx) * _value;
        var cat = LegendaryRuntime.Catalog;
        int n = CombatQuery.GetNearbyDamageables(pos, radius, Self(ctx), 20, _buf);
        for (int i = 0; i < n; i++)
        {
            CombatQuery.DealSynergyDamage(_buf[i], dmg, Self(ctx), element: RuneElement.Fire);
            LegendaryRuntime.SpawnVfx(cat?.fireAoeHitVfx, _buf[i].transform.position, Quaternion.identity, 1.5f, 0.8f);
        }
        if (n > 0)
            Managers.Sound?.PlayEffectAsync(s_hitSfx[Random.Range(0, s_hitSfx.Length)]).Forget();

        float vfxScale = 0.4f + extra * 0.15f;
        LegendaryRuntime.SpawnVfx(cat?.fireAoeVfx, pos, Quaternion.identity, 3f, vfxScale, alpha: 0.18f, yScale: 0.1f);
    }

    private int ExtraTiers() =>
        Mathf.Max(0, (MerlinRuneBridge.Instance?.GetZoneTier("FIRE") ?? 0) - _baseTier);
}

// ── F-2. 불사조 강타 (단일기) ─────────────────────────────────────────
public sealed class FireLegendSingleEffect : ItemCombatEffectBase
{
    private static readonly string[] s_hitSfx = { SoundKey.Sfx.MonsterHit1, SoundKey.Sfx.MonsterHit2, SoundKey.Sfx.MonsterHit3 };

    private readonly List<GameObject> _buf = new();
    private int _hitCount;
    private int _baseTier;
    private bool _baseSet;
    private const int BaseStrikes = 6;
    private CancellationTokenSource _cts;

    public FireLegendSingleEffect(ItemEffectSlot s) : base(s) { }

    public override void OnActivate(ItemEffectContext ctx)
    {
        LegendaryRuntime.EnsureExists();
        _hitCount = 0;
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

    public override void OnPostDealDamage(ItemEffectContext ctx, DamageReport report)
    {
        if (!_baseSet) { _baseTier = MerlinRuneBridge.Instance?.GetZoneTier("FIRE") ?? 0; _baseSet = true; }
        int extra    = ExtraTiers();
        int required = Mathf.Max(1, (int)_value2 - extra);

        if (++_hitCount < required) return;
        _hitCount = 0;
        if (ctx?.Player == null || report.Target == null || _cts == null) return;

        StrikeAsync(ctx, report.Target, BaseStrikes, _cts.Token).Forget();
    }

    private async UniTaskVoid StrikeAsync(ItemEffectContext ctx, GameObject target, int count, CancellationToken ct)
    {
        try
        {
            var cat = LegendaryRuntime.Catalog;
            float hitDmg    = EffAtk(ctx) * _value / count;
            float splashDmg = EffAtk(ctx) * _value3;

            for (int i = 0; i < count; i++)
            {
                if (ct.IsCancellationRequested || target == null) break;

                float yaw   = i * 360f / count + Random.Range(-20f, 20f);
                float pitch = Random.Range(-40f, -12f);
                Vector3 tPos    = target.transform.position;
                Vector3 spawn   = tPos + Quaternion.Euler(pitch, yaw, 0) * Vector3.forward * 1.2f;
                Vector3 dir     = tPos - spawn;
                Quaternion rot  = dir.sqrMagnitude > 0.01f ? Quaternion.LookRotation(dir) : Quaternion.identity;

                LegendaryRuntime.SpawnVfx(cat?.fireSingleVfx, spawn, rot, 1.5f);
                CombatQuery.DealSynergyDamage(target, hitDmg, ctx?.Player?.gameObject, element: RuneElement.Fire);
                LegendaryRuntime.SpawnVfx(cat?.fireSingleHitVfx, tPos, Quaternion.identity, 1.5f, 0.8f);
                Managers.Sound?.PlayEffectAsync(s_hitSfx[Random.Range(0, s_hitSfx.Length)]).Forget();

                // 마지막 타: 스플래시 + 피격 VFX
                if (i == count - 1)
                {
                    int n = CombatQuery.GetNearbyDamageables(tPos, 2f, target, 10, _buf);
                    for (int j = 0; j < n; j++)
                        CombatQuery.DealSynergyDamage(_buf[j], splashDmg, ctx?.Player?.gameObject, element: RuneElement.Fire);
                    LegendaryRuntime.SpawnVfx(cat?.fireHitVfx, tPos, Quaternion.identity, 2f);
                }

                await UniTask.Delay(400, cancellationToken: ct);
            }
        }
        catch (System.OperationCanceledException) { }
    }

    private int ExtraTiers() =>
        Mathf.Max(0, (MerlinRuneBridge.Instance?.GetZoneTier("FIRE") ?? 0) - _baseTier);
}

// ── F-3. 화염 오브 (투사체) ───────────────────────────────────────────
public sealed class FireLegendProjectileEffect : ItemCombatEffectBase
{
    private readonly List<GameObject> _orbs = new();
    private int _baseTier;
    private int _lastExtra = -1;
    private bool _baseSet;

    public FireLegendProjectileEffect(ItemEffectSlot s) : base(s) { }

    public override void OnActivate(ItemEffectContext ctx)
    {
        LegendaryRuntime.EnsureExists();
        _baseTier  = 0;
        _lastExtra = -1;
        _baseSet   = false;
    }

    public override void OnDeactivate() => ClearOrbs();

    public override void OnRoomEnter(ItemEffectContext ctx)
    {
        ClearOrbs();
        _baseSet = false;
    }

    public override void OnTick(ItemEffectContext ctx, float deltaTime)
    {
        if (!_baseSet)
        {
            _baseTier  = MerlinRuneBridge.Instance?.GetZoneTier("FIRE") ?? 0;
            _lastExtra = ExtraTiers();
            _baseSet   = true;
            SpawnOrbs(ctx);
            return;
        }
        int extra = ExtraTiers();
        if (extra == _lastExtra) return;
        _lastExtra = extra;
        ClearOrbs();
        SpawnOrbs(ctx);
    }

    private void SpawnOrbs(ItemEffectContext ctx)
    {
        if (ctx?.Player == null) return;
        int extra = ExtraTiers();
        int count = (int)_value3 + extra;
        float dmg = EffAtk(ctx) * _value;

        for (int i = 0; i < count; i++)
        {
            float angle = i * 360f / count + 45f + Random.Range(-10f, 10f);
            Vector3 dir = Quaternion.Euler(0, angle, 0) * Vector3.forward;
            Vector3 pos = LegendaryRuntime.GetRandomNavPoint(LegendaryProjectileBase.FlyHeight);

            var go    = new GameObject("FireOrb");
            var agent = go.AddComponent<FireOrbAgent>();
            agent.Init(this, ctx, dmg, 7f, 9999f, RuneElement.Fire, dir, maxBounces: 99999, pierce: true);
            go.transform.position = pos;
            _orbs.Add(go);
        }
    }

    private void ClearOrbs()
    {
        LegendaryRuntime.Instance?.ClearAgentsOf(this);
        _orbs.Clear();
    }

    private int ExtraTiers() =>
        Mathf.Max(0, (MerlinRuneBridge.Instance?.GetZoneTier("FIRE") ?? 0) - _baseTier);
}
