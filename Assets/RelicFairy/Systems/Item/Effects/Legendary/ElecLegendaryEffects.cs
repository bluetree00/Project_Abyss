using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using RelicFairy.Monster;
using UnityEngine;

// ── 번개 전설 3종 — 「몇 초마다」를 없애고 빌드 행동으로 발동(10-14 룬 재설계 R3) ──────────
// 천둥 폭격 = 연격 · 과전류 포박 = 필살 · 전자기 구체 = 주문. 존 단계마다 피해 +15%(개수형은 +1) — LegendaryTuning.

// ── T-1. 천둥 폭격 (연격) ─────────────────────────────────────────────
/// <summary>
/// 맞힌 15번마다(value2) 15 m 안 적 3명(value3)에게 낙뢰 — 공격력 × value(100%), 다음 3명에게 절반 연쇄.
/// 무적인 적을 헛친 타(피해 0)는 세지 않는다.
/// </summary>
public sealed class ElecLegendAoeEffect : ItemCombatEffectBase
{
    private static readonly string[] s_hitSfx = { SoundKey.Sfx.MonsterHit1, SoundKey.Sfx.MonsterHit2, SoundKey.Sfx.MonsterHit3 };

    private readonly List<GameObject> _buf = new();
    private int _hits;

    /// <summary>새 행(발동 「OnHitCount」)인가 — 옛 CDN 행이면 코드 기본값을 쓴다.</summary>
    private bool NewRow => _trigger == "OnHitCount";

    public ElecLegendAoeEffect(ItemEffectSlot s) : base(s) { }

    public override void OnActivate(ItemEffectContext ctx)
    {
        LegendaryRuntime.EnsureExists();
        _hits = 0;
    }

    public override void OnPostDealDamage(ItemEffectContext ctx, DamageReport report)
    {
        if (report.DamageDealt <= 0f || ctx?.Player == null) return;
        int required = Mathf.Max(1, Mathf.RoundToInt(LegendaryTuning.Pick(NewRow, _value2, 15f)));
        if (++_hits < required) return;
        _hits = 0;
        Strike(ctx);
    }

    private void Strike(ItemEffectContext ctx)
    {
        int targets    = Mathf.Max(1, Mathf.RoundToInt(LegendaryTuning.Pick(NewRow, _value3, 3f)));
        Vector3 pos    = ctx.Player.transform.position;
        float mainDmg  = LegendaryTuning.Atk(ctx) * LegendaryTuning.Pick(NewRow, _value, 1f) * LegendaryTuning.TierMul("ELECTRIC");
        float chainDmg = mainDmg * 0.5f;

        int n = CombatQuery.GetNearbyDamageables(pos, 15f, Self(ctx), targets * 2, _buf);
        var cat = LegendaryRuntime.Catalog;
        int hitCount = Mathf.Min(n, targets);
        Quaternion strikeRot = Quaternion.Euler(180f, 0f, 0f); // 수직 반전: 위에서 아래로

        for (int i = 0; i < hitCount; i++)
        {
            CombatQuery.DealSynergyDamage(_buf[i], mainDmg, Self(ctx), element: RuneElement.Electric);
            LegendaryRuntime.SpawnVfx(cat?.elecAoeHitVfx, _buf[i].transform.position, Quaternion.identity, 1.5f, 0.8f);
            LegendaryRuntime.SpawnVfx(cat?.elecAoeVfx, _buf[i].transform.position + Vector3.up * 4f, strikeRot, 2f, 0.5f);

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
}

// ── T-2. 과전류 포박 (필살) ───────────────────────────────────────────
/// <summary>
/// 치명 또는 강타가 터지면(쿨 value2 = 6초) 그 대상을 1.5초 속박(보스 제외)하고 대상을 포함해 반경 3 m 폭발 — 그 타격 피해 × value(150%).
/// </summary>
public sealed class ElecLegendSingleEffect : ItemCombatEffectBase
{
    private static readonly string[] s_hitSfx = { SoundKey.Sfx.MonsterHit1, SoundKey.Sfx.MonsterHit2, SoundKey.Sfx.MonsterHit3 };
    private const float BindSeconds = 1.5f, ExplosionRadius = 3f;

    private readonly List<GameObject> _buf = new();
    private float _readyAt;
    private CancellationTokenSource _cts;

    /// <summary>새 행(발동 「OnCrit」)인가 — 옛 CDN 행이면 코드 기본값을 쓴다.</summary>
    private bool NewRow => _trigger == "OnCrit";

    public ElecLegendSingleEffect(ItemEffectSlot s) : base(s) { }

    public override void OnActivate(ItemEffectContext ctx)
    {
        LegendaryRuntime.EnsureExists();
        _readyAt = 0f;
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
        if (!(report.IsCrit || report.IsHeavy) || report.DamageDealt <= 0f || report.Target == null) return;
        if (ctx?.Player == null || _cts == null || Time.time < _readyAt) return;
        _readyAt = Time.time + LegendaryTuning.Pick(NewRow, _value2, 6f);
        float boom = report.DamageDealt * LegendaryTuning.Pick(NewRow, _value, 1.5f) * LegendaryTuning.TierMul("ELECTRIC");
        BindAndExplodeAsync(ctx, report.Target, boom, _cts.Token).Forget();
    }

    private async UniTaskVoid BindAndExplodeAsync(ItemEffectContext ctx, GameObject target, float boom, CancellationToken ct)
    {
        try
        {
            var cat = LegendaryRuntime.Catalog;
            var mb = target.GetComponentInParent<MonsterBase>();
            if (mb != null && mb.Grade != MonsterGrade.Boss) mb.Status.ApplyCc("bind", BindSeconds);   // 이름대로 실제 속박
            Vector3 vfxCenter = target.transform.position + new Vector3(0f, 0f, -3f);   // Effect_16_ElectricField 중심 보정
            LegendaryRuntime.SpawnVfx(cat?.elecSingleVfx, vfxCenter, Quaternion.identity, BindSeconds, 0.6f);

            await UniTask.Delay(System.TimeSpan.FromSeconds(BindSeconds), cancellationToken: ct);
            if (target == null) return;

            Vector3 explPos = target.transform.position;
            CombatQuery.DealSynergyDamage(target, boom, ctx?.Player?.gameObject, element: RuneElement.Electric);   // 대상도 폭발에 든다
            int nb = CombatQuery.GetNearbyDamageables(explPos, ExplosionRadius, target, 10, _buf);
            for (int i = 0; i < nb; i++)
                CombatQuery.DealSynergyDamage(_buf[i], boom, ctx?.Player?.gameObject, element: RuneElement.Electric);
            LegendaryRuntime.SpawnVfx(cat?.elecHitVfx, explPos, Quaternion.identity, 2f, 0.3f);
            SkillRangeRing.Circle(explPos, ExplosionRadius);   // 섬광은 점 크기 — 폭발 판정은 외곽선으로(10-13 이펙트 = 판정)
            Managers.Sound?.PlayEffectAsync(s_hitSfx[Random.Range(0, s_hitSfx.Length)]).Forget();
        }
        catch (System.OperationCanceledException) { }
    }
}

// ── T-3. 전자기 구체 (주문) ───────────────────────────────────────────
/// <summary>
/// E · R 스킬을 쓸 때마다 구체 +1(최대 3 · 존 단계마다 +1 · 12초). 구체는 튕겨 다니며 닿은 적에게 공격력 × value(60%)(같은 대상 0.5초),
/// 스킬이 적을 맞힐 때마다 구체마다 볼트 한 발(0.3초에 한 번).
/// </summary>
public sealed class ElecLegendProjectileEffect : ItemCombatEffectBase
{
    private const int   BaseMax = 3;
    private const float OrbSeconds = 12f, HitGap = 0.5f, BoltGap = 0.3f;

    private readonly List<ElecOrbAgent> _orbs = new();
    private float _nextBoltAt;
    private ItemEffectContext _ctx;

    /// <summary>새 행(발동 「OnSkillUse」)인가 — 옛 CDN 행이면 코드 기본값을 쓴다.</summary>
    private bool NewRow => _trigger == "OnSkillUse";

    public ElecLegendProjectileEffect(ItemEffectSlot s) : base(s) { }

    public override void OnActivate(ItemEffectContext ctx)
    {
        LegendaryRuntime.EnsureExists();
        _ctx = ctx;
    }

    public override void OnDeactivate()
    {
        LegendaryRuntime.Instance?.ClearAgentsOf(this);
        _orbs.Clear();
    }

    public override void OnRoomEnter(ItemEffectContext ctx)
    {
        LegendaryRuntime.Instance?.ClearAgentsOf(this);
        _orbs.Clear();
    }

    public override void OnSkillUse(ItemEffectContext ctx, SkillType skill)
    {
        if (skill != SkillType.E && skill != SkillType.R) return;
        if (ctx?.Player == null) return;
        _ctx = ctx;
        _orbs.RemoveAll(o => o == null);
        int max = BaseMax + LegendaryTuning.Tier("ELECTRIC");
        while (_orbs.Count >= max) { if (_orbs[0] != null) Object.Destroy(_orbs[0].gameObject); _orbs.RemoveAt(0); }

        float angle = Random.Range(0f, 360f);
        Vector3 dir = Quaternion.Euler(0, angle, 0) * Vector3.forward;
        var go    = new GameObject("ElecBall");
        go.transform.position = ctx.Player.transform.position + Vector3.up * (LegendaryProjectileBase.FlyHeight + 0.3f);
        var agent = go.AddComponent<ElecOrbAgent>();
        agent.InitElec(this, ctx, 0f, LegendaryTuning.Pick(NewRow, _value3, 1.2f), 6f, OrbSeconds, dir);
        agent.TimedBolts   = false;
        agent.PerTargetGap = HitGap;
        agent.DamageFn     = OrbDamage;
        _orbs.Add(agent);
    }

    public override void OnPostDealDamage(ItemEffectContext ctx, DamageReport report)
    {
        if (!CombatDamage.IsSkillAction(report.ActionType) || report.ActionType == WeaponActionType.QSkill || report.DamageDealt <= 0f) return;
        if (Time.time < _nextBoltAt) return;
        _nextBoltAt = Time.time + BoltGap;
        _orbs.RemoveAll(o => o == null);
        foreach (var o in _orbs) o.FireBoltNow();
    }

    // 개수형 — 존 단계는 구체 수(+1)로 받는다(피해 배율은 붙이지 않는다)
    private float OrbDamage()
        => LegendaryTuning.Atk(_ctx) * LegendaryTuning.Pick(NewRow, _value, 0.6f);
}
