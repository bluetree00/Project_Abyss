using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

// ── T-1. 천둥 폭격 (광역기) ───────────────────────────────────────────
public sealed class ElecLegendAoeEffect : ItemCombatEffectBase
{
    private static readonly string[] s_hitSfx = { SoundKey.Sfx.MonsterHit1, SoundKey.Sfx.MonsterHit2, SoundKey.Sfx.MonsterHit3 };

    private readonly List<GameObject> _buf = new();
    private float _timer;
    private int _baseTier;
    private bool _baseSet;

    public ElecLegendAoeEffect(ItemEffectSlot s) : base(s) { }

    public override void OnActivate(ItemEffectContext ctx)
    {
        LegendaryRuntime.EnsureExists();
        _timer    = 0f;
        _baseTier = 0;
        _baseSet  = false;
    }

    public override void OnTick(ItemEffectContext ctx, float deltaTime)
    {
        if (!_baseSet) { _baseTier = MerlinRuneBridge.Instance?.GetZoneTier("ELECTRIC") ?? 0; _baseSet = true; }
        _timer += deltaTime;
        if (_timer < _value2) return;
        _timer -= _value2;
        if (ctx?.Player == null) return;

        int extra   = ExtraTiers();
        int targets = 3 + extra;

        Vector3 pos     = ctx.Player.transform.position;
        float mainDmg  = EffAtk(ctx) * _value;
        float chainDmg = mainDmg * 0.5f;

        int n = CombatQuery.GetNearbyDamageables(pos, 15f, Self(ctx), targets * 2, _buf);

        var cat = LegendaryRuntime.Catalog;
        int hitCount = Mathf.Min(n, targets);
        Quaternion strikeRot = Quaternion.Euler(180f, 0f, 0f); // 수직 반전: 위에서 아래로
        float vfxScale = 0.5f;

        for (int i = 0; i < hitCount; i++)
        {
            CombatQuery.DealSynergyDamage(_buf[i], mainDmg, Self(ctx), element: RuneElement.Electric);
            LegendaryRuntime.SpawnVfx(cat?.elecAoeHitVfx, _buf[i].transform.position, Quaternion.identity, 1.5f, 0.8f);
            // 각 타겟 머리 위 4m에서 번개 낙뢰
            Vector3 strikePos = _buf[i].transform.position + Vector3.up * 4f;
            LegendaryRuntime.SpawnVfx(cat?.elecAoeVfx, strikePos, strikeRot, 2f, vfxScale);

            int chainIdx = i + targets;
            if (chainIdx < n)
            {
                CombatQuery.DealSynergyDamage(_buf[chainIdx], chainDmg, Self(ctx), element: RuneElement.Electric);
                ItemGuide.Chain(_buf[i].transform.position, _buf[chainIdx].transform.position);
            }
        }
        if (hitCount > 0)
            Managers.Sound?.PlayEffectAsync(s_hitSfx[Random.Range(0, s_hitSfx.Length)]).Forget();
    }

    private int ExtraTiers() =>
        Mathf.Max(0, (MerlinRuneBridge.Instance?.GetZoneTier("ELECTRIC") ?? 0) - _baseTier);
}

// ── T-2. 과전류 포박 (단일기) ─────────────────────────────────────────
public sealed class ElecLegendSingleEffect : ItemCombatEffectBase
{
    private static readonly string[] s_hitSfx = { SoundKey.Sfx.MonsterHit1, SoundKey.Sfx.MonsterHit2, SoundKey.Sfx.MonsterHit3 };

    private readonly List<GameObject> _buf = new();
    private float _timer;
    private int _baseTier;
    private bool _baseSet;
    private const float BindDuration = 3f;
    private const float ExplosionRadius = 3f;
    private CancellationTokenSource _cts;

    public ElecLegendSingleEffect(ItemEffectSlot s) : base(s) { }

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
        if (!_baseSet) { _baseTier = MerlinRuneBridge.Instance?.GetZoneTier("ELECTRIC") ?? 0; _baseSet = true; }
        _timer += deltaTime;
        if (_timer < _value2) return;
        _timer -= _value2;
        if (ctx?.Player == null || _cts == null) return;

        int n = CombatQuery.GetNearbyDamageables(ctx.Player.transform.position, 20f, Self(ctx), 1, _buf);
        if (n == 0) return;

        BindAndExplodeAsync(ctx, _buf[0], ExtraTiers(), _cts.Token).Forget();
    }

    private async UniTaskVoid BindAndExplodeAsync(ItemEffectContext ctx, GameObject target, int extra, CancellationToken ct)
    {
        try
        {
            var cat = LegendaryRuntime.Catalog;
            float explosionR = ExplosionRadius + extra * 0.5f;
            float tickDmg    = EffAtk(ctx) * _value * 0.5f;
            float aoeEnd     = EffAtk(ctx) * 2f;
            // Z축 -3 오프셋: Effect_16_ElectricField가 몬스터 중심에 맞춰지도록 보정
            Vector3 vfxCenter = target.transform.position + new Vector3(0f, 0f, -3f);

            LegendaryRuntime.SpawnVfx(cat?.elecSingleVfx, vfxCenter, Quaternion.identity, BindDuration, 0.6f);

            int ticks = Mathf.RoundToInt(BindDuration / 0.5f);
            for (int t = 0; t < ticks; t++)
            {
                if (ct.IsCancellationRequested || target == null) break;
                CombatQuery.DealSynergyDamage(target, tickDmg, ctx?.Player?.gameObject, element: RuneElement.Electric);
                LegendaryRuntime.SpawnVfx(cat?.elecSingleHitVfx, target.transform.position, Quaternion.identity, 1.5f, 0.8f);
                Managers.Sound?.PlayEffectAsync(s_hitSfx[Random.Range(0, s_hitSfx.Length)]).Forget();
                await UniTask.Delay(500, cancellationToken: ct);
            }

            if (target != null)
            {
                Vector3 explPos = target.transform.position;
                int nb = CombatQuery.GetNearbyDamageables(explPos, explosionR, target, 10, _buf);
                for (int i = 0; i < nb; i++)
                    CombatQuery.DealSynergyDamage(_buf[i], aoeEnd, ctx?.Player?.gameObject, element: RuneElement.Electric);
                LegendaryRuntime.SpawnVfx(cat?.elecHitVfx, explPos, Quaternion.identity, 2f, 0.3f);
                Managers.Sound?.PlayEffectAsync(s_hitSfx[Random.Range(0, s_hitSfx.Length)]).Forget();
            }
        }
        catch (System.OperationCanceledException) { }
    }

    private int ExtraTiers() =>
        Mathf.Max(0, (MerlinRuneBridge.Instance?.GetZoneTier("ELECTRIC") ?? 0) - _baseTier);
}

// ── T-3. 전자기 구체 (투사체) ─────────────────────────────────────────
public sealed class ElecLegendProjectileEffect : ItemCombatEffectBase
{
    private int _baseTier;
    private int _lastExtra = -1;
    private bool _baseSet;
    private ItemEffectContext _savedCtx;

    public ElecLegendProjectileEffect(ItemEffectSlot s) : base(s) { }

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
            _baseTier  = MerlinRuneBridge.Instance?.GetZoneTier("ELECTRIC") ?? 0;
            _lastExtra = ExtraTiers();
            _baseSet   = true;
            SpawnBalls(ctx);
            return;
        }
        int extra = ExtraTiers();
        if (extra == _lastExtra) return;
        _lastExtra = extra;
        LegendaryRuntime.Instance?.ClearAgentsOf(this);
        SpawnBalls(ctx);
    }

    private void SpawnBalls(ItemEffectContext ctx)
    {
        if (ctx?.Player == null) return;
        int extra      = ExtraTiers();
        int count      = Mathf.RoundToInt(_value2) + extra;
        float dmg      = EffAtk(ctx) * _value;
        float explMult = _value3;

        float flyY = LegendaryProjectileBase.FlyHeight + 0.3f;
        for (int i = 0; i < count; i++)
        {
            float angle = i * 360f / count + 45f + Random.Range(-10f, 10f);
            Vector3 dir = Quaternion.Euler(0, angle, 0) * Vector3.forward;
            Vector3 pos = LegendaryRuntime.GetRandomNavPoint(flyY);

            var go    = new GameObject("ElecBall");
            var agent = go.AddComponent<ElecOrbAgent>();
            agent.InitElec(this, ctx, dmg, explMult, 6f, 9999f, dir);
            go.transform.position = pos;
        }
    }

    private int ExtraTiers() =>
        Mathf.Max(0, (MerlinRuneBridge.Instance?.GetZoneTier("ELECTRIC") ?? 0) - _baseTier);
}
