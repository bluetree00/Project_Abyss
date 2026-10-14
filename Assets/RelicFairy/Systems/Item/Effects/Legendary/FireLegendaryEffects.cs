using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

// ── 불 전설 3종 — 「몇 초마다」를 없애고 빌드 행동으로 발동(10-14 룬 재설계 R3) ─────────────
// 마그마 분출 = 처치 · 불사조 강타 = 연격 · 화염 오브 = 기동. 존 단계마다 피해 +15%(개수형은 +1) — LegendaryTuning.

// ── F-1. 마그마 분출 (처치) ───────────────────────────────────────────
/// <summary>
/// 쓰러뜨린 자리에서 반경 3 m 폭발(공격력 100%) — 폭발이 쓰러뜨리면 또 터진다(연쇄 · 한 번에 3겹까지).
/// value = 공격력 배율 · value3 = 반경(새 행).
/// </summary>
public sealed class FireLegendAoeEffect : ItemCombatEffectBase
{
    private static readonly string[] s_hitSfx = { SoundKey.Sfx.MonsterHit1, SoundKey.Sfx.MonsterHit2, SoundKey.Sfx.MonsterHit3 };
    private const int MaxChain = 3;
    private static int s_depth;   // 연쇄 깊이 — 폭발 → 처치 → 폭발이 같은 프레임에 끝없이 이어지지 않게

    private readonly List<GameObject> _buf = new();

    /// <summary>새 행(발동 「OnKill」)인가 — 옛 CDN 행이면 코드 기본값을 쓴다.</summary>
    private bool NewRow => _trigger == "OnKill";

    public FireLegendAoeEffect(ItemEffectSlot s) : base(s) { }

    public override void OnActivate(ItemEffectContext ctx) => LegendaryRuntime.EnsureExists();

    public override void OnKill(ItemEffectContext ctx, GameObject target)
    {
        if (ctx?.Player == null || target == null || s_depth >= MaxChain) return;
        float radius = LegendaryTuning.Pick(NewRow, _value3, 3f);
        float dmg    = LegendaryTuning.Atk(ctx) * LegendaryTuning.Pick(NewRow, _value, 1f) * LegendaryTuning.TierMul("FIRE");
        Vector3 pos  = target.transform.position;
        var cat = LegendaryRuntime.Catalog;

        s_depth++;
        try
        {
            int n = CombatQuery.GetNearbyDamageables(pos, radius, Self(ctx), 20, _buf);
            var hit = new List<GameObject>(_buf);   // 피해 중 처치 → 재귀가 _buf를 다시 쓴다
            for (int i = 0; i < n; i++)
            {
                if (hit[i] == null) continue;
                CombatQuery.DealSynergyDamage(hit[i], dmg, Self(ctx), element: RuneElement.Fire);
                LegendaryRuntime.SpawnVfx(cat?.fireAoeHitVfx, hit[i].transform.position, Quaternion.identity, 1.5f, 0.8f);
            }
            LegendaryRuntime.SpawnVfx(cat?.fireAoeVfx, pos, Quaternion.identity, 2f, radius * 0.1333f, alpha: 0.18f, yScale: 0.1f);
            SkillRangeRing.Circle(pos, radius);
            if (n > 0) Managers.Sound?.PlayEffectAsync(s_hitSfx[Random.Range(0, s_hitSfx.Length)]).Forget();
        }
        finally { s_depth--; }
    }
}

// ── F-2. 불사조 강타 (연격) ───────────────────────────────────────────
/// <summary>
/// 맞힌 12번마다(value2) 불사조가 6번 내리꽂힌다 — 합계 공격력 × value · 공속 +1%마다 피해 +1%.
/// 무적인 적을 헛친 타(피해 0)는 세지 않는다. 마지막 타 반경 2 m 공격력 × value3.
/// </summary>
public sealed class FireLegendSingleEffect : ItemCombatEffectBase
{
    private static readonly string[] s_hitSfx = { SoundKey.Sfx.MonsterHit1, SoundKey.Sfx.MonsterHit2, SoundKey.Sfx.MonsterHit3 };

    private readonly List<GameObject> _buf = new();
    private int _hitCount;
    private const int BaseStrikes = 6;
    private CancellationTokenSource _cts;

    /// <summary>새 행(발동 「OnHitCount」)인가 — 옛 CDN 행이면 코드 기본값을 쓴다.</summary>
    private bool NewRow => _trigger == "OnHitCount";

    public FireLegendSingleEffect(ItemEffectSlot s) : base(s) { }

    public override void OnActivate(ItemEffectContext ctx)
    {
        LegendaryRuntime.EnsureExists();
        _hitCount = 0;
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
        if (report.DamageDealt <= 0f) return;   // 무적 대상 헛침은 안 센다
        int required = Mathf.Max(1, Mathf.RoundToInt(_value2 > 0f ? _value2 : 12f));
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
            // 공속 +1%마다 피해 +1% — 연타 빌드의 축(넘친 공속도 배율 상한 2.0까지)
            float asMul     = ctx.Stats != null ? ctx.Stats.AttackSpeedMultiplier : 1f;
            float tier      = LegendaryTuning.TierMul("FIRE");
            float hitDmg    = LegendaryTuning.Atk(ctx) * _value * asMul * tier / count;
            float splashDmg = LegendaryTuning.Atk(ctx) * _value3 * tier;

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
                // 마지막 타는 아래 피격 폭발 하나로 — 같은 점 · 같은 프레임 두 겹(10-11 이펙트 톤 점검)
                if (i < count - 1)
                    LegendaryRuntime.SpawnVfx(cat?.fireSingleHitVfx, tPos, Quaternion.identity, 1.5f, 0.8f);
                Managers.Sound?.PlayEffectAsync(s_hitSfx[Random.Range(0, s_hitSfx.Length)]).Forget();

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
}

// ── F-3. 화염 오브 (기동) ─────────────────────────────────────────────
/// <summary>
/// 플레이어 둘레를 도는 오브 3개(존 단계마다 +1) — 닿은 적에게 공격력 × value(0.5) × 이동속도 배율, 대시 뒤 1초는 ×2.
/// 같은 대상은 0.5초에 한 번. 피해는 맞힐 때의 공격력으로 다시 센다.
/// </summary>
public sealed class FireLegendProjectileEffect : ItemCombatEffectBase
{
    private const int   BaseOrbs   = 3;
    private const float OrbitRadius = 2.2f, OrbitDegPerSec = 150f, DashBoostSeconds = 1f, DashBoostMul = 2f, HitGap = 0.5f;

    private readonly List<GameObject> _orbs = new();
    private int _lastCount = -1;
    private bool _spawned;
    private float _dashUntil = -999f;
    private ItemEffectContext _ctx;

    /// <summary>새 행(발동 「OnDash」)인가 — 옛 CDN 행이면 코드 기본값을 쓴다.</summary>
    private bool NewRow => _trigger == "OnDash";

    public FireLegendProjectileEffect(ItemEffectSlot s) : base(s) { }

    public override void OnActivate(ItemEffectContext ctx)
    {
        LegendaryRuntime.EnsureExists();
        _ctx = ctx;
        _lastCount = -1;
        _spawned   = false;
    }

    public override void OnDeactivate() => ClearOrbs();

    public override void OnRoomEnter(ItemEffectContext ctx)
    {
        ClearOrbs();
        _spawned = false;
    }

    public override void OnRollEnd(ItemEffectContext ctx) => _dashUntil = Time.time + DashBoostSeconds;

    public override void OnTick(ItemEffectContext ctx, float deltaTime)
    {
        _ctx = ctx;
        int count = BaseOrbs + LegendaryTuning.Tier("FIRE");
        if (_spawned && count == _lastCount) return;
        ClearOrbs();
        _lastCount = count;
        _spawned   = true;
        SpawnOrbs(ctx, count);
    }

    private float OrbDamage()
    {
        if (_ctx == null) return 0f;
        float move = _ctx.Stats != null ? _ctx.Stats.MoveSpeedMultiplier : 1f;
        float dash = Time.time < _dashUntil ? DashBoostMul : 1f;
        return LegendaryTuning.Atk(_ctx) * LegendaryTuning.Pick(NewRow, _value, 0.5f) * Mathf.Max(0.5f, move) * dash;
    }

    private void SpawnOrbs(ItemEffectContext ctx, int count)
    {
        if (ctx?.Player == null) return;
        Vector3 c = ctx.Player.transform.position;
        for (int i = 0; i < count; i++)
        {
            float angle = i * 360f / count;
            var go    = new GameObject("FireOrb");
            go.transform.position = new Vector3(c.x, LegendaryProjectileBase.FlyHeight + c.y, c.z);
            var agent = go.AddComponent<FireOrbAgent>();
            agent.Init(this, ctx, 0f, 0f, 9999f, RuneElement.Fire, Vector3.forward, maxBounces: 99999, pierce: true, hitRadius: 0.6f);
            agent.PerTargetGap = HitGap;
            agent.DamageFn     = OrbDamage;
            agent.SetOrbit(ctx.Player.transform, OrbitRadius, angle, OrbitDegPerSec);
            _orbs.Add(go);
        }
    }

    private void ClearOrbs()
    {
        LegendaryRuntime.Instance?.ClearAgentsOf(this);
        _orbs.Clear();
    }
}
