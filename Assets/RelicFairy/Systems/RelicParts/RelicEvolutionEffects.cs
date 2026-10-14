using System.Collections.Generic;
using RelicFairy.Monster;
using UnityEngine;

// ═══════════════════════════════════════════════════════════
// 유물 진화 6종(10-14 빌드 시너지 C) — 조각 효과 틀 위에 얹는다(허브 사건 · 같은 활성 · 해제 길).
// 정의 · 조건은 RelicEvolution. 적중의 강타 · 원거리 여부는 HitInfo.IsHeavy · IsRanged(룬 디스패처가 적중 보고에서 옮긴다).
// ═══════════════════════════════════════════════════════════

/// <summary>진화를 얻는 순간 한 줄 알림 — 「유물 진화 — 이름 · 계열 각인 +2」.</summary>
internal static class RelicEvolutionNotice
{
    public static void Show(string id)
    {
        var d = RelicEvolution.ById(id);
        if (d == null) return;
        // 계열 칩 + 유물 문장 줄(83 UI · 10-14) — 글은 같다. HUD가 런마다 한 번만 낸다(챕터마다 새 플레이어가 이 효과를 다시 묶는다).
        ItemEffectVfxHelper.ShowBuildNotice(BuildNotice.Evolution(d));
    }
}

// ── 가웨인 ───────────────────────────────────────────────

/// <summary>타오르는 정오(연격) — 정오 동안 적중마다 태양흔(같은 적 간격 없음). 넘친 공속 → 태양 화상(GawainSolarBurnPassive가 BurnMul로 읽는다).</summary>
public sealed class GEvoBlazingNoonEffect : GawainFragment
{
    public GEvoBlazingNoonEffect() : base("evo_gawain_combo") { }
    protected override void Bind()   { Hub.Hit += OnHubHit; RelicEvolutionNotice.Show(EffectKey); }
    protected override void Unbind() { Hub.Hit -= OnHubHit; }

    private void OnHubHit(HitInfo h)
    {
        if (!Hub.IsNoon) return;
        var mb = AsMonster(h.Target);
        if (mb != null && !mb.IsDead) Hub.AddSunmark(mb, 1, ignoreGap: true);
    }
}

/// <summary>천정의 일격(필살) — 정오에 강타 · 치명이 그 자리에 작은 낙일(1초에 한 번).</summary>
public sealed class GEvoZenithStrikeEffect : GawainFragment
{
    private const float Gap = 1f, Fraction = 0.25f, RadiusScale = 0.5f;
    private float _readyAt;

    public GEvoZenithStrikeEffect() : base("evo_gawain_crit") { }
    protected override void Bind()   { Hub.Hit += OnHubHit; RelicEvolutionNotice.Show(EffectKey); }
    protected override void Unbind() { Hub.Hit -= OnHubHit; }

    private void OnHubHit(HitInfo h)
    {
        if (!Hub.IsNoon || !(h.IsCritical || h.IsHeavy) || h.Target == null || Time.time < _readyAt) return;
        if (h.ActionType == WeaponActionType.QSkill) return;   // 낙일이 낙일을 부르지 않게
        _readyAt = Time.time + Gap;
        SolarDescentSkillRuntime.StrikeSmallSun(Player, Hub.Relic, h.Target.transform.position, Fraction, RadiusScale);
    }
}

/// <summary>태양의 화살(사격) — 정오에 원거리 명중 5번마다 그 자리에 작은 해.</summary>
public sealed class GEvoSunArrowEffect : GawainFragment
{
    private const int Every = 5;
    private const float Fraction = 0.2f, RadiusScale = 0.45f;
    private int _count;

    public GEvoSunArrowEffect() : base("evo_gawain_ranged") { }
    protected override void Bind()   { Hub.Hit += OnHubHit; RelicEvolutionNotice.Show(EffectKey); }
    protected override void Unbind() { Hub.Hit -= OnHubHit; }

    private void OnHubHit(HitInfo h)
    {
        if (!Hub.IsNoon || !h.IsRanged || h.Target == null) return;
        if (++_count < Every) return;
        _count = 0;
        SolarDescentSkillRuntime.StrikeSmallSun(Player, Hub.Relic, h.Target.transform.position, Fraction, RadiusScale);
    }
}

// ── 랜슬롯 ───────────────────────────────────────────────

/// <summary>끝없는 광란(연격) — 광기를 1.5배로(2타마다 광기 +1 더) · 광란 중 10타마다 광란 +1초.</summary>
public sealed class LEvoEndlessFrenzyEffect : LancelotFragment
{
    private const int ExtendEvery = 10;
    private const float ExtendSeconds = 1f;
    private int _hits, _frenzyHits;

    public LEvoEndlessFrenzyEffect() : base("evo_lancelot_combo") { }
    protected override void Bind()   { Hub.Hit += OnHubHit; RelicEvolutionNotice.Show(EffectKey); }
    protected override void Unbind() { Hub.Hit -= OnHubHit; }

    private void OnHubHit(HitInfo h)
    {
        var relic = Hub.Relic;
        if (relic == null) return;
        if (relic.IsFrenzy)
        {
            if (++_frenzyHits >= ExtendEvery) { _frenzyHits = 0; relic.Madness?.ExtendFrenzy(ExtendSeconds); }
            return;
        }
        _frenzyHits = 0;
        if (++_hits % 2 == 0) relic.AddStack(1);   // 적중 1타 = 광기 1 → 1.5
    }
}

/// <summary>심판자(필살) — 강타가 낙인 2개 · 심판 마무리 둘레 3 m에 치명 몫(마무리 피해 50%)을 더 준다.</summary>
public sealed class LEvoJudgeEffect : LancelotFragment
{
    private const float FinisherRadius = 3f, CritShare = 0.5f;

    public LEvoJudgeEffect() : base("evo_lancelot_crit") { }
    protected override void Bind()   { Hub.Hit += OnHubHit; Hub.FinisherLanded += OnFinisher; RelicEvolutionNotice.Show(EffectKey); }
    protected override void Unbind() { Hub.Hit -= OnHubHit; Hub.FinisherLanded -= OnFinisher; }

    private void OnHubHit(HitInfo h)
    {
        if (!h.IsHeavy || h.ActionType == WeaponActionType.QSkill) return;
        var mb = AsMonster(h.Target);
        if (mb != null && !mb.IsDead) Hub.AddBrand(mb, 2, overflowToGrudge: true, ignoreGap: true);
    }

    private void OnFinisher(Vector3 pos, Vector3 fwd)
    {
        var relic = Hub.Relic;
        if (relic == null || Player == null) return;
        float bonus = relic.LastFinisherDamage * CritShare;
        if (bonus <= 0f) return;
        foreach (var mb in Near(pos, FinisherRadius, Owner, 16))
            CombatQuery.DealSynergyDamage(mb, bonus, Owner, 1f, isCrit: true);
    }
}

/// <summary>배신의 화살(사격) — 원거리 적중이 낙인을 새긴다(같은 적 0.3초에 한 번).</summary>
public sealed class LEvoTraitorArrowEffect : LancelotFragment
{
    private const float Gap = 0.3f;
    private readonly Dictionary<int, float> _last = new();

    public LEvoTraitorArrowEffect() : base("evo_lancelot_ranged") { }
    protected override void Bind()   { Hub.Hit += OnHubHit; RelicEvolutionNotice.Show(EffectKey); }
    protected override void Unbind() { Hub.Hit -= OnHubHit; }

    private void OnHubHit(HitInfo h)
    {
        if (!h.IsRanged) return;
        var mb = AsMonster(h.Target);
        if (mb == null || mb.IsDead) return;
        int id = mb.GetInstanceID();
        if (_last.TryGetValue(id, out float at) && Time.time - at < Gap) return;
        _last[id] = Time.time;
        Hub.AddBrand(mb, 1, overflowToGrudge: true, ignoreGap: true);
    }
}
