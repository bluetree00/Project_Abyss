using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using RelicFairy.Monster;
using UnityEngine;

// ── 번개 전설 3종 — 「몇 초마다」를 없애고 빌드 행동으로 발동(10-14 룬 재설계 R3) ──────────
// 천둥 폭격 = 연격 · 과전류 포박 = 필살 · 전자기 구체 = 주문. 존 단계마다 피해 +15%(개수형은 +1) — LegendaryTuning.

// ── T-1. 번개 사슬 (연격 · 옛 천둥 폭격) ─────────────────────────────────
/// <summary>
/// 10-24 개편 — 맞힌 value2(15)번마다 그 적과 6 m 안 적 최대 value3(3)명을 번개 사슬로 3초 잇는다:
/// 묶인 적 하나가 맞으면 나머지도 그 피해의 value(4%) + 1.5%p × 존 단계(2차 피해라 다시 나누지 않는다 · 되먹임 없음 · 실측 95 맞춤 — 무리를 치면 사슬이 거의 늘 걸린다).
/// 묶을 적이 없으면(보스 · 혼자) 그 적 과충전 4초(받는 피해 +20%). 각성(번개 존 4단계) = 사슬이 끝날 때 묶인 적마다 공격력 100% 낙뢰.
/// </summary>
public sealed class ElecLegendAoeEffect : ItemCombatEffectBase
{
    private const float LinkRadius = 6f, LinkSeconds = 3f, OverchargeAmp = 0.2f, OverchargeSeconds = 4f, BeamGap = 0.2f, SharePerTier = 0.015f;

    private readonly List<MonsterBase> _near = new();
    private readonly List<MonsterBase> _linked = new();
    private float _linkUntil = -1f, _beamReadyAt;
    private int _hits;

    /// <summary>새 행(발동 「OnHitChain」)인가 — 옛 CDN 행이면 코드 기본값을 쓴다.</summary>
    private bool NewRow => _trigger == "OnHitChain";

    public ElecLegendAoeEffect(ItemEffectSlot s) : base(s) { }

    public override void OnActivate(ItemEffectContext ctx)
    {
        LegendaryRuntime.EnsureExists();
        _hits = 0;
        _linked.Clear();
        _linkUntil = -1f;
    }

    public override void OnPostDealDamage(ItemEffectContext ctx, DamageReport report)
    {
        if (report.DamageDealt <= 0f || ctx?.Player == null || report.Target == null) return;
        var mb = report.Target.GetComponentInParent<MonsterBase>();
        if (_linkUntil > 0f && mb != null && _linked.Contains(mb)) Share(ctx, mb, report.DamageDealt);
        int required = Mathf.Max(1, Mathf.RoundToInt(LegendaryTuning.Pick(NewRow, _value2, 15f)));
        if (++_hits < required) return;
        _hits = 0;
        if (mb != null && !mb.IsDead) Link(ctx, mb);
    }

    public override void OnTick(ItemEffectContext ctx, float deltaTime)
    {
        if (_linkUntil < 0f || Time.time < _linkUntil) return;
        _linkUntil = -1f;
        if (LegendaryTuning.Awakened("ELECTRIC") && ctx?.Player != null)   // 각성 — 사슬이 끝나며 낙뢰
        {
            float dmg = LegendaryTuning.Atk(ctx) * LegendaryTuning.TierMul("ELECTRIC");
            foreach (var m in _linked)
            {
                if (m == null || m.IsDead) continue;
                CombatQuery.DealSynergyDamage(m, dmg, Self(ctx), element: RuneElement.Electric);
                ElementVfxPlayer.PlayBurst(RuneElement.Electric, m.transform.position, 1f);
            }
        }
        _linked.Clear();
    }

    private void Link(ItemEffectContext ctx, MonsterBase first)
    {
        _linked.Clear();
        _linked.Add(first);
        int want = Mathf.Max(1, Mathf.RoundToInt(LegendaryTuning.Pick(NewRow, _value3, 3f)));
        int n = CombatQuery.GetNearbyEnemies(first.transform.position, LinkRadius, Self(ctx), 16, _near);
        for (int i = 0; i < n && _linked.Count <= want; i++)
        {
            var m = _near[i];
            if (m != null && !m.IsDead && m != first) _linked.Add(m);
        }
        if (_linked.Count == 1)   // 묶을 적이 없다(보스 · 혼자) — 과충전
        {
            first.ApplyDamageTakenAmp(OverchargeAmp, OverchargeSeconds, "overcharge");
            ElementVfxPlayer.PlayBurst(RuneElement.Electric, first.transform.position, 1f);
            _linked.Clear();
            return;
        }
        _linkUntil = Time.time + LinkSeconds;
        for (int i = 1; i < _linked.Count; i++)
            ElementVfxPlayer.PlayBeam(RuneElement.Electric, first.transform.position + Vector3.up, _linked[i].transform.position + Vector3.up, 0.3f);
    }

    private void Share(ItemEffectContext ctx, MonsterBase src, float dealt)
    {
        float share = dealt * (LegendaryTuning.Pick(NewRow, _value, 0.04f) + SharePerTier * LegendaryTuning.Tier("ELECTRIC"));
        bool beam = Time.time >= _beamReadyAt;
        if (beam) _beamReadyAt = Time.time + BeamGap;
        foreach (var m in _linked)
        {
            if (m == null || m == src || m.IsDead) continue;
            CombatQuery.DealSynergyDamage(m, share, Self(ctx), element: RuneElement.Electric);
            if (beam) ElementVfxPlayer.PlayBeam(RuneElement.Electric, src.transform.position + Vector3.up, m.transform.position + Vector3.up, 0.15f);
        }
    }
}

// ── T-2. 축전 포박 (필살 · 옛 과전류 포박) ───────────────────────────────
/// <summary>
/// 10-24 개편 — 치명 · 강타 때(쿨 value2 = 6초) 그 적 1.5초 속박(보스는 속박 없이 모으기만) — 속박 동안 그 적이 받은 피해의 value3(40%) + 5%p × 존 단계를 모아
/// 끝날 때 반경 3 m 폭발 = 모은 양 + 그 타격 × value(1.5) × 존 배율. 묶어 놓고 몰아치는 창. 각성(번개 존 4단계) = 폭발에 맞은 적도 0.75초 속박.
/// </summary>
public sealed class ElecLegendSingleEffect : ItemCombatEffectBase
{
    private static readonly string[] s_hitSfx = { SoundKey.Sfx.MonsterHit1, SoundKey.Sfx.MonsterHit2, SoundKey.Sfx.MonsterHit3 };
    private const float BindSeconds = 1.5f, ExplosionRadius = 3f, AwakeBind = 0.75f, StorePerTier = 0.05f;

    private readonly List<GameObject> _buf = new();
    private float _readyAt, _stored;
    private MonsterBase _bound;
    private CancellationTokenSource _cts;

    /// <summary>새 행(발동 「OnCritStore」)인가 — 옛 CDN 행이면 코드 기본값을 쓴다.</summary>
    private bool NewRow => _trigger == "OnCritStore";

    public ElecLegendSingleEffect(ItemEffectSlot s) : base(s) { }

    public override void OnActivate(ItemEffectContext ctx)
    {
        LegendaryRuntime.EnsureExists();
        _readyAt = 0f;
        _bound = null;
        _cts = new CancellationTokenSource();
    }

    public override void OnDeactivate()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
        _bound = null;
    }

    public override void OnPostDealDamage(ItemEffectContext ctx, DamageReport report)
    {
        if (report.DamageDealt <= 0f || report.Target == null) return;
        var mb = report.Target.GetComponentInParent<MonsterBase>();
        if (_bound != null && mb == _bound)   // 속박 동안 받은 피해를 모은다
            _stored += report.DamageDealt * (LegendaryTuning.Pick(NewRow, _value3, 0.4f) + StorePerTier * LegendaryTuning.Tier("ELECTRIC"));
        if (!(report.IsCrit || report.IsHeavy) || ctx?.Player == null || _cts == null || _bound != null || Time.time < _readyAt) return;
        _readyAt = Time.time + LegendaryTuning.Pick(NewRow, _value2, 6f);
        float boom = report.DamageDealt * LegendaryTuning.Pick(NewRow, _value, 1.5f) * LegendaryTuning.TierMul("ELECTRIC");
        BindAndExplodeAsync(ctx, report.Target, mb, boom, _cts.Token).Forget();
    }

    private async UniTaskVoid BindAndExplodeAsync(ItemEffectContext ctx, GameObject target, MonsterBase mb, float boom, CancellationToken ct)
    {
        try
        {
            var cat = LegendaryRuntime.Catalog;
            _bound = mb;
            _stored = 0f;
            if (mb != null && mb.Grade != MonsterGrade.Boss) mb.Status.ApplyCc("bind", BindSeconds);   // 이름대로 실제 속박
            Vector3 vfxCenter = target.transform.position + new Vector3(0f, 0f, -3f);   // Effect_16_ElectricField 중심 보정
            LegendaryRuntime.SpawnVfx(cat?.elecSingleVfx, vfxCenter, Quaternion.identity, BindSeconds, 0.6f);

            await UniTask.Delay(System.TimeSpan.FromSeconds(BindSeconds), cancellationToken: ct);
            _bound = null;
            if (target == null) return;

            float total = boom + _stored;
            Vector3 explPos = target.transform.position;
            CombatQuery.DealSynergyDamage(target, total, ctx?.Player?.gameObject, element: RuneElement.Electric);   // 대상도 폭발에 든다
            bool awake = LegendaryTuning.Awakened("ELECTRIC");
            int nb = CombatQuery.GetNearbyDamageables(explPos, ExplosionRadius, target, 10, _buf);
            for (int i = 0; i < nb; i++)
            {
                CombatQuery.DealSynergyDamage(_buf[i], total, ctx?.Player?.gameObject, element: RuneElement.Electric);
                if (!awake) continue;
                var o = _buf[i] != null ? _buf[i].GetComponentInParent<MonsterBase>() : null;   // 각성 — 폭발이 묶는다
                if (o != null && !o.IsDead && o.Grade != MonsterGrade.Boss) o.Status.ApplyCc("bind", AwakeBind);
            }
            ElementVfxPlayer.PlayBurst(RuneElement.Electric, explPos, ExplosionRadius);   // 광역 그림 = 폭발 반경
            SkillRangeRing.Circle(explPos, ExplosionRadius);
            Managers.Sound?.PlayEffectAsync(s_hitSfx[Random.Range(0, s_hitSfx.Length)]).Forget();
        }
        catch (System.OperationCanceledException) { _bound = null; }
    }
}

// ── T-3. 뇌전 탄창 (주문 · 옛 전자기 구체) ───────────────────────────────
/// <summary>
/// 10-24 개편 — E · R을 쓸 때마다 구체 +1(최대 value2 = 3 · 둘레를 돈다). 평타가 적을 맞히면 구체 하나가 그 적에게 날아가
/// 공격력 × value(120% · 존 배율) + 감전(받는 피해 +value3 = 10% · 3초) — 한 휘두름에 한 발. 다 쏘면 다음 스킬까지 없다 → 스킬과 평타를 섞는 리듬.
/// 각성(번개 존 4단계) = 최대 5 · 날아간 구체가 반경 2 m로 터진다. HUD = LegendaryHud "orb".
/// </summary>
public sealed class ElecLegendProjectileEffect : ItemCombatEffectBase
{
    private const int   AwakeMax = 5;
    private const float OrbitRadius = 1.3f, OrbitDegPerSec = 220f, OrbScale = 0.5f, ShockSeconds = 3f, BurstRadius = 2f, SameSwing = 0.08f;
    private const string HudKey = "orb", ShockId = "elec_shock";

    private readonly List<GameObject> _buf = new();
    private LegendOrbitRing _ring;
    private ItemEffectContext _ctx;
    private float _lastShotAt = -999f;

    /// <summary>새 행(발동 「OnSkillOrb」)인가 — 옛 CDN 행이면 코드 기본값을 쓴다.</summary>
    private bool NewRow => _trigger == "OnSkillOrb";

    private int Max => LegendaryTuning.Awakened("ELECTRIC") ? AwakeMax : Mathf.Max(1, Mathf.RoundToInt(LegendaryTuning.Pick(NewRow, _value2, 3f)));

    public ElecLegendProjectileEffect(ItemEffectSlot s) : base(s) { }

    public override void OnActivate(ItemEffectContext ctx)
    {
        LegendaryRuntime.EnsureExists();
        _ctx = ctx;
        Publish();
    }

    public override void OnDeactivate()
    {
        LegendaryRuntime.Instance?.ClearAgentsOf(this);
        _ring = null;
        LegendaryHud.Clear(HudKey);
    }

    public override void OnSkillUse(ItemEffectContext ctx, SkillType skill)
    {
        if ((skill != SkillType.E && skill != SkillType.R) || ctx?.Player == null) return;
        _ctx = ctx;
        if (_ring == null) _ring = LegendOrbitRing.Spawn(this, ctx.Player.transform, LegendaryRuntime.Catalog?.elecBallVfx, OrbScale, OrbitRadius, OrbitDegPerSec);
        if (_ring.ReadyCount < Max) _ring.Add();
        Publish();
    }

    public override void OnPostDealDamage(ItemEffectContext ctx, DamageReport report)
    {
        if (_ring == null || CombatDamage.IsSkillAction(report.ActionType) || report.DamageDealt <= 0f || report.Target == null) return;
        if (Time.time - _lastShotAt < SameSwing) return;   // 한 휘두름에 한 발
        _ctx = ctx;
        if (!_ring.Launch(report.Target, -1f, OnOrbArrive)) return;
        _lastShotAt = Time.time;
        Publish();
    }

    private void OnOrbArrive(GameObject target)
    {
        if (target == null || _ctx?.Player == null) return;
        float dmg = LegendaryTuning.Atk(_ctx) * LegendaryTuning.Pick(NewRow, _value, 1.2f) * LegendaryTuning.TierMul("ELECTRIC");
        GameObject self = _ctx.Player.gameObject;
        Vector3 at = target.transform.position;
        Shock(target, dmg, self);
        if (LegendaryTuning.Awakened("ELECTRIC"))   // 각성 — 날아간 구체가 터진다
        {
            int n = CombatQuery.GetNearbyDamageables(at, BurstRadius, self, 12, _buf);
            for (int i = 0; i < n; i++) if (_buf[i] != target) Shock(_buf[i], dmg, self);
            ElementVfxPlayer.PlayBurst(RuneElement.Electric, at, BurstRadius);
        }
        else ElementVfxPlayer.PlayBurst(RuneElement.Electric, at);
    }

    private void Shock(GameObject go, float dmg, GameObject self)
    {
        CombatQuery.DealSynergyDamage(go, dmg, self, element: RuneElement.Electric);
        var mb = go != null ? go.GetComponentInParent<MonsterBase>() : null;
        if (mb != null && !mb.IsDead) mb.ApplyDamageTakenAmp(LegendaryTuning.Pick(NewRow, _value3, 0.1f), ShockSeconds, ShockId);
    }

    private void Publish() => LegendaryHud.Set(HudKey, _ring != null ? _ring.ReadyCount : 0, Max);
}
