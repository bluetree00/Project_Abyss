using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using RelicFairy.Monster;
using UnityEngine;

// ── 얼음 전설 3종 — 「몇 초마다」를 없애고 빌드 행동으로 발동(10-14 룬 재설계 R3) ──────────
// 빙하 파동 = 주문 · 얼음 창 폭격 = 필살 · 영구 빙판 = 기동. 존 단계마다 피해 +15% — LegendaryTuning.

// ── I-1. 얼려 깨는 주문 (주문 · 옛 빙하 파동) ───────────────────────────
/// <summary>
/// 10-24 개편 — E · R이 맞힌 적은 서리 없이 바로 빙결(value2 = 1.5초 · 같은 적 6초에 한 번 · 보스는 받는 피해 +15% 서리 결정 3초).
/// 빙결된 적을 깨는 다음 적중 = 분쇄: 그 타격 × value(2)가 되도록 더하고 반경 2.5 m 공격력 × value3(80%) 파편 · 빙결을 끝낸다.
/// 룬 빙결(얼음 존 2단계)로 얼린 적도 깨진다. 존 단계마다 +20% · 각성(얼음 존 4단계) = 파편에 맞은 적도 빙결(같은 적 6초).
/// </summary>
public sealed class IceLegendAoeEffect : ItemCombatEffectBase
{
    private const string FreezeId = "freeze", FreezeCd = "legend_freeze_cd", CrystalId = "frost_crystal";
    private const float FreezeGap = 6f, ShardRadius = 2.5f, CrystalAmp = 0.15f, CrystalSeconds = 3f;
    private const float ShatterAfter = 0.25f;   // 얼린 그 스킬의 다음 틱이 바로 깨지 않게 — 얼음을 보고 깬다

    private readonly List<GameObject> _buf = new();
    private readonly Dictionary<MonsterBase, float> _frozenAt = new();

    /// <summary>새 행(발동 「OnSkillFreeze」)인가 — 옛 CDN 행이면 코드 기본값을 쓴다.</summary>
    private bool NewRow => _trigger == "OnSkillFreeze";

    public IceLegendAoeEffect(ItemEffectSlot s) : base(s) { }

    public override void OnActivate(ItemEffectContext ctx)
    {
        LegendaryRuntime.EnsureExists();
        _frozenAt.Clear();
    }

    public override void OnPostDealDamage(ItemEffectContext ctx, DamageReport report)
    {
        if (report.DamageDealt <= 0f || report.Target == null || ctx?.Player == null) return;
        var mb = report.Target.GetComponentInParent<MonsterBase>();
        if (mb == null || mb.IsDead) return;

        if (mb.Status.HasCc(FreezeId))
        {
            if (!_frozenAt.TryGetValue(mb, out float at) || Time.time - at >= ShatterAfter) Shatter(ctx, mb, report.DamageDealt);
            return;
        }

        bool skill = report.ActionType == WeaponActionType.ESkill || report.ActionType == WeaponActionType.RSkill;
        if (!skill || mb.Status.IsOnCooldown(FreezeCd)) return;
        Freeze(mb);
    }

    private void Freeze(MonsterBase mb)
    {
        mb.Status.SetCooldown(FreezeCd, FreezeGap);
        if (mb.Grade == MonsterGrade.Boss) { mb.ApplyDamageTakenAmp(CrystalAmp, CrystalSeconds, CrystalId); return; }   // 보스는 서리 결정
        mb.Status.ApplyCc(FreezeId, LegendaryTuning.Pick(NewRow, _value2, 1.5f));   // 몸 얼음 그림은 상태가 붙인다
        if (_frozenAt.Count > 32) _frozenAt.Clear();
        _frozenAt[mb] = Time.time;
    }

    private void Shatter(ItemEffectContext ctx, MonsterBase mb, float hit)
    {
        mb.Status.ClearCc(FreezeId);
        _frozenAt.Remove(mb);
        float tier  = LegendaryTuning.TierMul("ICE");
        float extra = hit * Mathf.Max(0f, LegendaryTuning.Pick(NewRow, _value, 2f) - 1f) * tier;
        if (extra > 0f) CombatQuery.DealSynergyDamage(mb, extra, Self(ctx), element: RuneElement.Ice);
        float shard = LegendaryTuning.Atk(ctx) * LegendaryTuning.Pick(NewRow, _value3, 0.8f) * tier;
        bool awake  = LegendaryTuning.Awakened("ICE");
        Vector3 at  = mb.transform.position;
        int n = CombatQuery.GetNearbyDamageables(at, ShardRadius, mb.gameObject, 16, _buf);
        for (int i = 0; i < n; i++)
        {
            CombatQuery.DealSynergyDamage(_buf[i], shard, Self(ctx), element: RuneElement.Ice);
            if (!awake) continue;
            var other = _buf[i] != null ? _buf[i].GetComponentInParent<MonsterBase>() : null;   // 각성 — 파편이 얼린다
            if (other != null && !other.IsDead && !other.Status.IsOnCooldown(FreezeCd)) Freeze(other);
        }
        ElementVfxPlayer.PlayBurst(RuneElement.Ice, at, ShardRadius);   // 파편 범위 = 판정
    }
}

// ── I-2. 서리 처형 (필살 · 옛 얼음 창 폭격) ─────────────────────────────
/// <summary>
/// 10-24 개편 — 치명 · 강타가 맞힌 적 체력이 value2(15%) + 2%p × 존 단계 이하면 바로 쓰러뜨린다(일반 · 정예).
/// 그 위거나 보스면 얼음 창 5발 = 그 타격 × value(1.0)(같은 적 value3 = 6초 · 보스는 문턱 아래에서 ×2 더). 모든 치명 · 강타가 서리 1중첩(실측 95 맞춤).
/// 각성(얼음 존 4단계) = 처형된 적이 얼어 터지며 반경 3 m 공격력 100% + 서리 2.
/// </summary>
public sealed class IceLegendSingleEffect : ItemCombatEffectBase
{
    private static readonly string[] s_hitSfx = { SoundKey.Sfx.MonsterHit1, SoundKey.Sfx.MonsterHit2, SoundKey.Sfx.MonsterHit3 };
    private const int    Spears = 5, FrostMax = 2;
    private const string FrostId = "frost", SpearCd = "legend_frost_spear";
    private const float  FrostMag = 0.12f, FrostSeconds = 3f, BlastRadius = 3f, ThresholdPerTier = 0.02f;

    private readonly List<GameObject> _buf = new();
    private CancellationTokenSource _cts;

    /// <summary>새 행(발동 「OnCritExecute」)인가 — 옛 CDN 행이면 코드 기본값을 쓴다.</summary>
    private bool NewRow => _trigger == "OnCritExecute";

    public IceLegendSingleEffect(ItemEffectSlot s) : base(s) { }

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

    public override void OnPostDealDamage(ItemEffectContext ctx, DamageReport report)
    {
        if (!(report.IsCrit || report.IsHeavy) || report.DamageDealt <= 0f || report.Target == null) return;
        if (ctx?.Player == null || _cts == null) return;
        var mb = report.Target.GetComponentInParent<MonsterBase>();
        if (mb == null || mb.IsDead) return;
        mb.Status.ApplySlow(FrostId, FrostMag, FrostSeconds, FrostMax);   // 모든 치명 · 강타 = 서리 1

        float ratio = mb.EffectiveMaxHp > 0 ? (float)mb.CurrentHp / mb.EffectiveMaxHp : 1f;
        float threshold = LegendaryTuning.Pick(NewRow, _value2, 0.15f) + ThresholdPerTier * LegendaryTuning.Tier("ICE");
        bool boss = mb.Grade == MonsterGrade.Boss;
        if (!boss && ratio <= threshold) { Execute(ctx, mb); return; }

        if (mb.Status.IsOnCooldown(SpearCd)) return;
        mb.Status.SetCooldown(SpearCd, LegendaryTuning.Pick(NewRow, _value3, 6f));
        float mult = LegendaryTuning.Pick(NewRow, _value, 1f) * LegendaryTuning.TierMul("ICE");
        if (boss && ratio <= threshold) mult *= 2f;   // 보스는 처형 대신 문턱 아래에서 창이 두 배
        StrikeAsync(ctx, report.Target, report.DamageDealt * mult / Spears, _cts.Token).Forget();
    }

    private void Execute(ItemEffectContext ctx, MonsterBase mb)
    {
        Vector3 at = mb.transform.position;
        CombatQuery.DealSynergyDamage(mb, mb.CurrentHp * 2f + 10f, Self(ctx), element: RuneElement.Ice);
        ElementVfxPlayer.PlayBurst(RuneElement.Ice, at, 1f);   // 얼어 부서지는 한 점
        if (!LegendaryTuning.Awakened("ICE")) return;
        float dmg = LegendaryTuning.Atk(ctx) * LegendaryTuning.TierMul("ICE");   // 각성 — 얼어 터진다
        int n = CombatQuery.GetNearbyDamageables(at, BlastRadius, mb.gameObject, 16, _buf);
        for (int i = 0; i < n; i++)
        {
            CombatQuery.DealSynergyDamage(_buf[i], dmg, Self(ctx), element: RuneElement.Ice);
            var o = _buf[i] != null ? _buf[i].GetComponentInParent<MonsterBase>() : null;
            if (o == null || o.IsDead) continue;
            o.Status.ApplySlow(FrostId, FrostMag, FrostSeconds, FrostMax);
            o.Status.ApplySlow(FrostId, FrostMag, FrostSeconds, FrostMax);
        }
        ElementVfxPlayer.PlayBurst(RuneElement.Ice, at, BlastRadius);
    }

    private async UniTaskVoid StrikeAsync(ItemEffectContext ctx, GameObject target, float spearDmg, CancellationToken ct)
    {
        try
        {
            var cat = LegendaryRuntime.Catalog;
            for (int i = 0; i < Spears; i++)
            {
                if (ct.IsCancellationRequested || target == null) break;

                float yaw   = i * 360f / Spears + Random.Range(-20f, 20f);
                float pitch = Random.Range(-60f, -20f);
                Vector3 tPos   = target.transform.position;
                Vector3 spawn  = tPos + Quaternion.Euler(pitch, yaw, 0) * Vector3.forward * 1.2f;
                Vector3 dir    = tPos - spawn;
                Quaternion rot = dir.sqrMagnitude > 0.01f ? Quaternion.LookRotation(dir) : Quaternion.identity;

                LegendaryRuntime.SpawnVfx(cat?.iceSingleVfx, tPos + Vector3.up * 0.6f, rot, 1.0f, 0.15f);   // 맞은 자리에 창 방향으로 박힌다(10-18 실측 68)
                CombatQuery.DealSynergyDamage(target, spearDmg, ctx?.Player?.gameObject, element: RuneElement.Ice);
                LegendaryRuntime.SpawnVfx(cat?.iceHitVfx, tPos, Quaternion.identity, 1.5f, 0.67f);   // 대상 타격 ≈ 1 m(10-22 B2 실측 83: ×0.8 = 1.2 m)
                Managers.Sound?.PlayEffectAsync(s_hitSfx[Random.Range(0, s_hitSfx.Length)]).Forget();

                await UniTask.Delay(150, cancellationToken: ct);
            }
        }
        catch (System.OperationCanceledException) { }
    }
}

// ── I-3. 얼음 무대 (기동 · 옛 영구 빙판) ─────────────────────────────────
/// <summary>
/// 10-24 개편 — 대시 끝 자리에 빙판(반경 value3 = 2.5 m · 5초 · 최대 2). 빙판 위 적: 이동 −40% · 받는 피해 +value2(20%).
/// 빙판 위 적이 빙결되면 빙판째 깨지며 공격력 × value(150%) 광역. 저스트 회피면 빙판이 바로 깨진다(빙결 1초 + 깨짐 피해).
/// 존 단계마다 +20% · 각성(얼음 존 4단계) = 반경 +1 m(3.5) · 최대 3.
/// </summary>
public sealed class IceLegendFieldEffect : ItemCombatEffectBase
{
    private const float FieldSeconds = 5f, SlowMag = 0.4f, DodgeFreeze = 1f, AwakeRadius = 1f;
    private const int   MaxFields = 2, AwakeMaxFields = 3;

    private readonly List<IceZoneField> _fields = new();
    private PlayerController _dodgeSource;

    /// <summary>새 행(발동 「OnDashStage」)인가 — 옛 CDN 행이면 코드 기본값을 쓴다.</summary>
    private bool NewRow => _trigger == "OnDashStage";

    public IceLegendFieldEffect(ItemEffectSlot s) : base(s) { }

    public override void OnActivate(ItemEffectContext ctx)
    {
        LegendaryRuntime.EnsureExists();
        if (ctx?.Player == null) return;
        _dodgeSource = ctx.Player;
        _dodgeSource.OnPerfectDodge += HandlePerfectDodge;
    }

    public override void OnDeactivate()
    {
        if (_dodgeSource != null) _dodgeSource.OnPerfectDodge -= HandlePerfectDodge;
        _dodgeSource = null;
        LegendaryRuntime.Instance?.ClearAgentsOf(this);
        _fields.Clear();
    }

    public override void OnRoomEnter(ItemEffectContext ctx)
    {
        LegendaryRuntime.Instance?.ClearAgentsOf(this);
        _fields.Clear();
    }

    public override void OnRollEnd(ItemEffectContext ctx)
    {
        if (ctx?.Player == null) return;
        bool awake = LegendaryTuning.Awakened("ICE");
        int max = awake ? AwakeMaxFields : MaxFields;
        _fields.RemoveAll(f => f == null);
        while (_fields.Count >= max) { if (_fields[0] != null) Object.Destroy(_fields[0].gameObject); _fields.RemoveAt(0); }

        float radius  = LegendaryTuning.Pick(NewRow, _value3, 2.5f) + (awake ? AwakeRadius : 0f);
        float shatter = LegendaryTuning.Atk(ctx) * LegendaryTuning.Pick(NewRow, _value, 1.5f) * LegendaryTuning.TierMul("ICE");
        float amp     = LegendaryTuning.Pick(NewRow, _value2, 0.2f);
        var go = new GameObject("IceStage");
        go.transform.position = ctx.Player.transform.position;
        var zone = go.AddComponent<IceZoneField>();
        zone.InitStage(this, ctx, radius, FieldSeconds, SlowMag, amp, shatter);
        _fields.Add(zone);
    }

    private void HandlePerfectDodge(float _)
    {
        _fields.RemoveAll(f => f == null);
        foreach (var f in _fields.ToArray()) f.Shatter(DodgeFreeze);   // 저스트 회피 — 빙판이 바로 깨진다
        _fields.Clear();
    }
}
