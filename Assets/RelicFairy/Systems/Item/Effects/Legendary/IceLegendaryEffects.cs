using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using RelicFairy.Monster;
using UnityEngine;

// ── 얼음 전설 3종 — 「몇 초마다」를 없애고 빌드 행동으로 발동(10-14 룬 재설계 R3) ──────────
// 빙하 파동 = 주문 · 얼음 창 폭격 = 필살 · 영구 빙판 = 기동. 존 단계마다 피해 +15% — LegendaryTuning.

// ── I-1. 빙하 파동 (주문) ─────────────────────────────────────────────
/// <summary>
/// E · R 스킬을 쓰면(쿨 value2 = 4초) 플레이어 둘레 3.5 m(value3)에 냉기 파동 4번(공격력 × value = 40%) — 첫 파동이 서리 2중첩을 건다.
/// </summary>
public sealed class IceLegendAoeEffect : ItemCombatEffectBase
{
    private static readonly string[] s_hitSfx = { SoundKey.Sfx.MonsterHit1, SoundKey.Sfx.MonsterHit2, SoundKey.Sfx.MonsterHit3 };
    private const int   WaveTicks = 4;
    private const string FrostId = "frost";
    private const float FrostSlow = 0.25f, FrostSeconds = 2.5f;

    private readonly List<GameObject> _buf = new();
    private float _readyAt;
    private CancellationTokenSource _cts;

    /// <summary>새 행(발동 「OnSkillUse」)인가 — 옛 CDN 행이면 코드 기본값을 쓴다.</summary>
    private bool NewRow => _trigger == "OnSkillUse";

    public IceLegendAoeEffect(ItemEffectSlot s) : base(s) { }

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

    public override void OnSkillUse(ItemEffectContext ctx, SkillType skill)
    {
        if (skill != SkillType.E && skill != SkillType.R) return;   // 유물 Q는 제 몫이 따로 있다
        if (ctx?.Player == null || _cts == null || Time.time < _readyAt) return;
        _readyAt = Time.time + LegendaryTuning.Pick(NewRow, _value2, 4f);

        float radius = LegendaryTuning.Pick(NewRow, _value3, 3.5f);
        Vector3 pos  = ctx.Player.transform.position;
        var cat = LegendaryRuntime.Catalog;
        if (CombatQuery.GetNearbyDamageables(pos, radius, Self(ctx), 1, _buf) > 0)
            LegendaryRuntime.SpawnVfx(cat?.iceAoeVfx, pos, Quaternion.identity, 4f, radius * 0.0488f, alpha: 0.18f, yScale: 0.1f, follow: ctx.Player.transform);
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
                float dmg = LegendaryTuning.Atk(ctx) * LegendaryTuning.Pick(NewRow, _value, 0.4f) * LegendaryTuning.TierMul("ICE");
                Vector3 pos = ctx.Player.transform.position;
                int n = CombatQuery.GetNearbyDamageables(pos, radius, Self(ctx), 30, _buf);
                for (int i = 0; i < n; i++)
                {
                    CombatQuery.DealSynergyDamage(_buf[i], dmg, Self(ctx), element: RuneElement.Ice);
                    if (t != 0) continue;
                    // 첫 파동 — 서리 2중첩(얼음 존의 빙결 조건과 같은 통화) · 타격 이펙트는 첫 틱만(10-11 톤 2차 V-10)
                    var mb = _buf[i] != null ? _buf[i].GetComponentInParent<MonsterBase>() : null;
                    if (mb != null) { mb.Status.ApplySlow(FrostId, FrostSlow, FrostSeconds, 2); mb.Status.ApplySlow(FrostId, FrostSlow, FrostSeconds, 2); }
                    LegendaryRuntime.SpawnVfx(cat?.iceAoeHitVfx, _buf[i].transform.position, Quaternion.identity, 1.5f, 0.8f);
                }
                if (n > 0) Managers.Sound?.PlayEffectAsync(s_hitSfx[Random.Range(0, s_hitSfx.Length)]).Forget();
                await UniTask.Delay(400, cancellationToken: ct);
            }
        }
        catch (System.OperationCanceledException) { }
    }
}

// ── I-2. 얼음 창 폭격 (필살) ──────────────────────────────────────────
/// <summary>
/// 치명 또는 강타가 터지면(쿨 value2 = 5초) 그 대상에게 얼음 창 5발 — 합계 = 그 타격 피해 × value(150%).
/// </summary>
public sealed class IceLegendSingleEffect : ItemCombatEffectBase
{
    private static readonly string[] s_hitSfx = { SoundKey.Sfx.MonsterHit1, SoundKey.Sfx.MonsterHit2, SoundKey.Sfx.MonsterHit3 };
    private const int Spears = 5;

    private float _readyAt;
    private CancellationTokenSource _cts;

    /// <summary>새 행(발동 「OnCrit」)인가 — 옛 CDN 행이면 코드 기본값을 쓴다.</summary>
    private bool NewRow => _trigger == "OnCrit";

    public IceLegendSingleEffect(ItemEffectSlot s) : base(s) { }

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
        _readyAt = Time.time + LegendaryTuning.Pick(NewRow, _value2, 5f);
        float total = report.DamageDealt * LegendaryTuning.Pick(NewRow, _value, 1.5f) * LegendaryTuning.TierMul("ICE");
        StrikeAsync(ctx, report.Target, total / Spears, _cts.Token).Forget();
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

                LegendaryRuntime.SpawnVfx(cat?.iceSingleVfx, spawn, rot, 1.0f, 0.1f);
                CombatQuery.DealSynergyDamage(target, spearDmg, ctx?.Player?.gameObject, element: RuneElement.Ice);
                LegendaryRuntime.SpawnVfx(cat?.iceHitVfx, tPos, Quaternion.identity, 1.5f, 0.8f);
                Managers.Sound?.PlayEffectAsync(s_hitSfx[Random.Range(0, s_hitSfx.Length)]).Forget();

                await UniTask.Delay(150, cancellationToken: ct);
            }
        }
        catch (System.OperationCanceledException) { }
    }
}

// ── I-3. 영구 빙판 (기동) ─────────────────────────────────────────────
/// <summary>
/// 대시가 끝난 자리에 반경 2 m 빙판 4초(초당 공격력 × value = 30% · 둔화). 최대 3개.
/// 저스트 회피를 하면 지금 빙판 위의 적이 모두 빙결(1.5초).
/// </summary>
public sealed class IceLegendFieldEffect : ItemCombatEffectBase
{
    private const float FieldRadius = 2f, FieldSeconds = 4f, FreezeSeconds = 1.5f;
    private const int   MaxFields = 3;

    private readonly List<IceZoneField> _fields = new();
    private PlayerController _dodgeSource;

    /// <summary>새 행(발동 「OnDash」)인가 — 옛 CDN 행이면 코드 기본값을 쓴다.</summary>
    private bool NewRow => _trigger == "OnDash";

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
        _fields.RemoveAll(f => f == null);
        while (_fields.Count >= MaxFields) { if (_fields[0] != null) Object.Destroy(_fields[0].gameObject); _fields.RemoveAt(0); }

        float atk = LegendaryTuning.Atk(ctx);
        float dps = atk * LegendaryTuning.Pick(NewRow, _value, 0.3f) * LegendaryTuning.TierMul("ICE");
        var go = new GameObject("IceZone");
        go.transform.position = ctx.Player.transform.position;
        var zone = go.AddComponent<IceZoneField>();
        zone.Init(this, ctx, dps, FieldRadius, FieldSeconds, atk);
        _fields.Add(zone);
        SkillRangeRing.Circle(go.transform.position, FieldRadius);
    }

    private void HandlePerfectDodge(float _)
    {
        _fields.RemoveAll(f => f == null);
        foreach (var f in _fields) f.FreezeInside(FreezeSeconds);
    }
}
