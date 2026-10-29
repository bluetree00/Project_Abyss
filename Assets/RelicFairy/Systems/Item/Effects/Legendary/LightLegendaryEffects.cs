using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using RelicFairy.Monster;
using UnityEngine;
using UnityEngine.AI;

// ── 빛 전설 3종 — 「몇 초마다」를 없애고 빌드 행동으로 발동(10-14 룬 재설계 R3) ───────────
// 신성 폭발 = 위기 · 성스러운 심판 = 필살 · 영원의 성검 = 사격. 존 단계마다 피해 +15%(개수형은 +1) — LegendaryTuning.

// ── L-1. 빛의 반격 (위기 · 옛 신성 폭발) ─────────────────────────────────
/// <summary>
/// 10-24 개편 — 맞으면(쿨 value2 = 6초) 최대 체력 value3(12%) + 2%p × 존 단계 보호막 + 둘레 3.5 m 빛 파동 4번(공격력 × value = 50%) + 밀어내기.
/// 보호막이 남아 있는 동안 공격력 +15%. 체력 40% 아래면 맞지 않아도 6초마다. 회복은 없다(정책 — 보호막만).
/// 각성(빛 존 4단계) = 이 보호막이 깨질 때 파동 한 번 더.
/// </summary>
public sealed class LightLegendAoeEffect : ItemCombatEffectBase
{
    private static readonly string[] s_hitSfx = { SoundKey.Sfx.MonsterHit1, SoundKey.Sfx.MonsterHit2, SoundKey.Sfx.MonsterHit3 };
    // 장판 그림 수명 — 피해는 0.4초 × 4틱(1.2초에 끝)인데 그림이 4초 남아 피해 없는 장판으로 읽혔다(10-18 이펙트 점검)
    private const float AoeVfxLife = 1.8f;
    private const int   WaveTicks = 4;
    private const float LowHp = 0.4f, PushDistance = 1.5f, Radius = 3.5f, ShieldPerTier = 0.02f, AtkWhileShield = 0.15f;

    private readonly List<GameObject> _buf = new();
    private float _readyAt;
    private bool _granted;   // 이 전설이 준 보호막이 아직 남았나(각성 — 깨질 때 파동)
    private CancellationTokenSource _cts;

    /// <summary>새 행(발동 「OnHurtShield」)인가 — 옛 CDN 행이면 코드 기본값을 쓴다.</summary>
    private bool NewRow => _trigger == "OnHurtShield";

    public LightLegendAoeEffect(ItemEffectSlot s) : base(s) { }

    public override void OnActivate(ItemEffectContext ctx)
    {
        LegendaryRuntime.EnsureExists();
        _readyAt = 0f;
        _granted = false;
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
        if (ctx == null) return;
        if (_granted && ctx.Stats != null && !ctx.Stats.HasShield)
        {
            _granted = false;
            if (LegendaryTuning.Awakened("LIGHT")) Waves(ctx);   // 각성 — 보호막이 깨질 때 파동 한 번 더
        }
        ctx.RefreshHpRatio();
        if (ctx.HpRatio <= LowHp) TryBurst(ctx);
    }

    public override void ContributeDynamicStats(ItemEffectContext ctx, ref ItemDynamicStats dyn)
    {
        if (ctx?.Stats != null && ctx.Stats.HasShield) dyn.attackPercent += AtkWhileShield;   // 보호막이 남아 있는 동안
    }

    private void TryBurst(ItemEffectContext ctx)
    {
        if (ctx?.Player == null || ctx.Stats == null || _cts == null || Time.time < _readyAt) return;
        _readyAt = Time.time + LegendaryTuning.Pick(NewRow, _value2, 6f);
        float ratio = LegendaryTuning.Pick(NewRow, _value3, 0.12f) + ShieldPerTier * LegendaryTuning.Tier("LIGHT");
        ctx.Stats.AddShield(ctx.Stats.MaxHp * ratio);
        _granted = ctx.Stats.HasShield;
        Waves(ctx);
    }

    private void Waves(ItemEffectContext ctx)
    {
        if (ctx?.Player == null || _cts == null) return;
        Vector3 pos = ctx.Player.transform.position;
        if (CombatQuery.GetNearbyDamageables(pos, Radius, Self(ctx), 1, _buf) == 0) return;   // 맞을 적이 없으면 파동은 아낀다(보호막은 이미 받았다)
        var cat = LegendaryRuntime.Catalog;
        LegendaryRuntime.SpawnVfx(cat?.lightAoeVfx, pos, Quaternion.identity, AoeVfxLife, Radius * 0.0377f, alpha: 0.18f, yScale: 0.1f, follow: ctx.Player.transform);
        WaveTickAsync(ctx, _cts.Token).Forget();
    }

    private async UniTaskVoid WaveTickAsync(ItemEffectContext ctx, CancellationToken ct)
    {
        try
        {
            var cat = LegendaryRuntime.Catalog;
            for (int t = 0; t < WaveTicks; t++)
            {
                if (ct.IsCancellationRequested || ctx.Player == null) break;
                float dmg = LegendaryTuning.Atk(ctx) * LegendaryTuning.Pick(NewRow, _value, 0.5f) * LegendaryTuning.TierMul("LIGHT");
                Vector3 pos = ctx.Player.transform.position;
                int n = CombatQuery.GetNearbyDamageables(pos, Radius, Self(ctx), 20, _buf);
                for (int i = 0; i < n; i++)
                {
                    CombatQuery.DealSynergyDamage(_buf[i], dmg, Self(ctx), element: RuneElement.Light);
                    if (t != 0) continue;
                    LegendaryRuntime.SpawnVfx(cat?.lightAoeHitVfx, _buf[i].transform.position, Quaternion.identity, 1.5f, 0.49f);   // 대상 타격 ≈ 1 m(10-22 B2 실측 83: ×0.8 = 1.6 m)
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

// ── L-2. 심판의 표식 (필살 · 옛 성스러운 심판) ───────────────────────────
/// <summary>
/// 10-24 개편 — 치명 · 강타가 그 적에게 빛 표식 1개(같은 적 0.4초에 한 번 · 표식 value3 = 8초 · 맞힐 때마다 갱신).
/// value2(5) − 필살 각인 단계(최소 3)개가 되면 하늘에서 심판 광선: 그 타격 × value(1.6) + 0.3 × 존 단계 · 반경 2.5 m 적에게 그 절반 · 표식 0.
/// 한 적을 몰아쳐 심판을 여는 결정. 각성(빛 존 4단계) = 광선 뒤 표식 2개가 남는다.
/// </summary>
public sealed class LightLegendSingleEffect : ItemCombatEffectBase
{
    private static readonly string[] s_hitSfx = { SoundKey.Sfx.MonsterHit1, SoundKey.Sfx.MonsterHit2, SoundKey.Sfx.MonsterHit3 };
    private const int   MinRequired = 3, AwakeKeep = 2;
    private const float SplashRadius = 2.5f, MultPerTier = 0.3f, MarkGap = 0.4f;   // 같은 적은 0.4초에 한 번 — 한 휘두름의 여러 적중 보고가 표식을 몇 개씩 올렸다(실측 95)

    private readonly List<GameObject> _buf = new();
    private readonly Dictionary<MonsterBase, (int n, float until)> _marks = new();

    /// <summary>새 행(발동 「OnCritMark」)인가 — 옛 CDN 행이면 코드 기본값을 쓴다.</summary>
    private bool NewRow => _trigger == "OnCritMark";

    public LightLegendSingleEffect(ItemEffectSlot s) : base(s) { }

    public override void OnActivate(ItemEffectContext ctx)
    {
        LegendaryRuntime.EnsureExists();
        _marks.Clear();
    }

    public override void OnDeactivate() => _marks.Clear();

    public override void OnPostDealDamage(ItemEffectContext ctx, DamageReport report)
    {
        if (!(report.IsCrit || report.IsHeavy) || report.DamageDealt <= 0f || report.Target == null || ctx?.Player == null) return;
        var mb = report.Target.GetComponentInParent<MonsterBase>();
        if (mb == null || mb.IsDead) return;
        if (_marks.Count > 32) _marks.Clear();

        float seconds = LegendaryTuning.Pick(NewRow, _value3, 8f);
        int have = _marks.TryGetValue(mb, out var m) && Time.time < m.until ? m.n : 0;
        if (have > 0 && Time.time < m.until - seconds + MarkGap) return;   // 같은 적 0.4초 안 — 이미 올렸다
        int add  = 1;   // 강타도 1개(대검은 모든 타가 강타라 2개면 세 타마다 심판 · 실측 95 +229%)
        int required = Mathf.Max(MinRequired, Mathf.RoundToInt(LegendaryTuning.Pick(NewRow, _value2, 5f)) - BuildImprint.Stage(BuildFamily.Crit));
        int now = have + add;
        if (now < required)
        {
            _marks[mb] = (now, Time.time + seconds);
            ElementVfxPlayer.AttachStatus(RuneElement.Light, mb.transform, seconds);   // 표식이 있는 동안 몸에 빛(갱신)
            return;
        }
        bool awake = LegendaryTuning.Awakened("LIGHT");
        if (awake) _marks[mb] = (AwakeKeep, Time.time + seconds);
        else _marks.Remove(mb);
        float mult = LegendaryTuning.Pick(NewRow, _value, 1.6f) + MultPerTier * LegendaryTuning.Tier("LIGHT");
        Judge(ctx, mb, report.DamageDealt * mult);
    }

    private void Judge(ItemEffectContext ctx, MonsterBase mb, float dmg)
    {
        var cat = LegendaryRuntime.Catalog;
        Vector3 tPos = mb.transform.position;
        if (cat?.lightBeamVfx != null)   // PurifierBeam 안의 ParticleBeam만 — 하늘에서 내리꽂는 한 줄
        {
            var parent = Object.Instantiate(cat.lightBeamVfx, Vector3.zero, Quaternion.identity);
            var beamTf = parent.transform.Find("Effect_28_ParticleBeam");
            if (beamTf != null)
            {
                beamTf.SetParent(null);
                beamTf.SetPositionAndRotation(tPos + Vector3.up * 4f, Quaternion.Euler(-90f, Random.Range(0f, 360f), 0f));
                beamTf.localScale = new Vector3(0.25f, 3f, 0.25f);
                Object.Destroy(beamTf.gameObject, 1.0f);
            }
            Object.Destroy(parent);
        }
        CombatQuery.DealSynergyDamage(mb, dmg, Self(ctx), element: RuneElement.Light);
        int n = CombatQuery.GetNearbyDamageables(tPos, SplashRadius, mb.gameObject, 16, _buf);
        for (int i = 0; i < n; i++)
            CombatQuery.DealSynergyDamage(_buf[i], dmg * 0.5f, Self(ctx), element: RuneElement.Light);
        ElementVfxPlayer.PlayBurst(RuneElement.Light, tPos, SplashRadius);   // 절반 피해 범위 = 판정
        Managers.Sound?.PlayEffectAsync(s_hitSfx[Random.Range(0, s_hitSfx.Length)]).Forget();
    }
}

// ── L-3. 호위 성검 (사격 · 옛 영원의 성검) ───────────────────────────────
/// <summary>
/// 10-24 개편 — 방에 들어서면 성검 2자루가 둘레를 돈다(원거리 명중 value2 = 8번마다 +1 · 최대 6).
/// 원거리가 적을 맞히면 쉬고 있는 성검 하나가 그 적에게 날아가 꽂힌다(공격력 × value = 60% · 존 배율 · 설계 120%는 실측 95 15초 창 존 2 +67%) — value3(2)초 뒤 돌아온다 → 쏘는 곳으로 칼이 따라간다.
/// 각성(빛 존 4단계) = 꽂힌 성검이 1초 뒤 반경 2 m 빛 폭발(공격력 40% — 설계 60%는 실측 95 존 4 +119%).
/// </summary>
public sealed class LightLegendProjectileEffect : ItemCombatEffectBase
{
    private const int   StartSwords = 2, MaxSwords = 6;
    private const float OrbitRadius = 1.6f, OrbitDegPerSec = 120f, SwordScale = 0.7f, BurstDelay = 1f, BurstRadius = 2f, BurstRatio = 0.4f;

    private readonly List<(float at, GameObject target, Vector3 pos)> _bursts = new();
    private readonly List<GameObject> _buf = new();
    private LegendOrbitRing _ring;
    private ItemEffectContext _ctx;
    private int _hits;
    private bool _spawned;

    /// <summary>새 행(발동 「OnRangedGuard」)인가 — 옛 CDN 행이면 코드 기본값을 쓴다.</summary>
    private bool NewRow => _trigger == "OnRangedGuard";

    public LightLegendProjectileEffect(ItemEffectSlot s) : base(s) { }

    public override void OnActivate(ItemEffectContext ctx)
    {
        LegendaryRuntime.EnsureExists();
        _ctx = ctx;
        _spawned = false;
    }

    public override void OnDeactivate() => Clear();

    public override void OnRoomEnter(ItemEffectContext ctx)
    {
        Clear();
        _spawned = false;
    }

    public override void OnTick(ItemEffectContext ctx, float deltaTime)
    {
        _ctx = ctx;
        if (!_spawned && ctx?.Player != null)
        {
            _spawned = true;
            _hits = 0;
            _ring = LegendOrbitRing.Spawn(this, ctx.Player.transform, LegendaryRuntime.Catalog?.lightSwordVfx, SwordScale, OrbitRadius, OrbitDegPerSec);
            _ring.Add(StartSwords);
        }
        for (int i = _bursts.Count - 1; i >= 0; i--)
        {
            if (Time.time < _bursts[i].at) continue;
            var b = _bursts[i];
            _bursts.RemoveAt(i);
            Burst(b.target != null ? b.target.transform.position : b.pos);
        }
    }

    public override void OnPostDealDamage(ItemEffectContext ctx, DamageReport report)
    {
        if (!report.IsRanged || report.DamageDealt <= 0f || report.Target == null || _ring == null) return;
        _ctx = ctx;
        _ring.Launch(report.Target, LegendaryTuning.Pick(NewRow, _value3, 2f), OnSwordArrive);
        int required = Mathf.Max(1, Mathf.RoundToInt(LegendaryTuning.Pick(NewRow, _value2, 8f)));
        if (++_hits < required) return;
        _hits = 0;
        if (_ring.Count < MaxSwords) _ring.Add();
    }

    private void OnSwordArrive(GameObject target)
    {
        if (target == null || _ctx?.Player == null) return;
        float dmg = LegendaryTuning.Atk(_ctx) * LegendaryTuning.Pick(NewRow, _value, 0.6f) * LegendaryTuning.TierMul("LIGHT");
        CombatQuery.DealSynergyDamage(target, dmg, _ctx.Player.gameObject, element: RuneElement.Light);
        LegendaryRuntime.SpawnVfx(LegendaryRuntime.Catalog?.lightHitVfx, target.transform.position, Quaternion.identity, 2f, 0.5f);   // 대상 타격 ≈ 1 m(10-22 B2 실측 83)
        if (LegendaryTuning.Awakened("LIGHT")) _bursts.Add((Time.time + BurstDelay, target, target.transform.position));
    }

    private void Burst(Vector3 at)
    {
        if (_ctx?.Player == null) return;
        float dmg = LegendaryTuning.Atk(_ctx) * BurstRatio * LegendaryTuning.TierMul("LIGHT");
        int n = CombatQuery.GetNearbyDamageables(at, BurstRadius, _ctx.Player.gameObject, 12, _buf);
        for (int i = 0; i < n; i++) CombatQuery.DealSynergyDamage(_buf[i], dmg, _ctx.Player.gameObject, element: RuneElement.Light);
        ElementVfxPlayer.PlayBurst(RuneElement.Light, at, BurstRadius);   // 빛 폭발 범위 = 판정
    }

    private void Clear()
    {
        LegendaryRuntime.Instance?.ClearAgentsOf(this);
        _ring = null;
        _bursts.Clear();
    }
}
