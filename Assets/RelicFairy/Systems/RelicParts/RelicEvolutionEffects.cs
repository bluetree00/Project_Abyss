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

/// <summary>
/// 진화 효과가 실제로 터진 순간(10-23 W4 「유물 진화가 체감되기 힘들다」) — 머리 위 진화 이름 한 줄(같은 진화 2.5초에 한 번) +
/// <see cref="Procced"/> 사건(HUD 진화 문장이 듣는다). 그림이 없던 진화(광기 ×1.5 · 광란 +1초 · 낙인 2)도 보이게.
/// </summary>
public static class RelicEvolutionCue
{
    private const float TextGap = 2.5f;
    private static readonly Dictionary<string, float> s_lastText = new();

    /// <summary>진화 id가 터졌다.</summary>
    public static event System.Action<string> Procced;

    public static void Proc(string id, PlayerController p, Color color)
    {
        Procced?.Invoke(id);
        if (p == null) return;
        if (s_lastText.TryGetValue(id, out float at) && Time.time - at < TextGap && Time.time >= at) return;
        s_lastText[id] = Time.time;
        var d = RelicEvolution.ById(id);
        if (d != null) RelicFloatText.Show(p.transform.position + Vector3.up * 0.6f, d.Name, color);
    }
}

// ── 가웨인 ───────────────────────────────────────────────

/// <summary>
/// 타오르는 정오(연격) — 정오 동안 적중마다 태양흔(같은 적 간격 없음) · 가득 찬 적은 그 자리에서 개화(같은 적 3초에 한 번).
/// 넘친 공속 → 태양 화상(GawainSolarBurnPassive가 BurnMul로 읽는다).
/// 예전엔 쌓기만 했는데 여명이 이미 상한까지 채워 정오에 쌓은 몫이 버려졌다(10-23 실측 7v ×1.01).
/// </summary>
public sealed class GEvoBlazingNoonEffect : GawainFragment
{
    private const float BloomGap = 3f;   // 같은 적 개화 간격 — 공속이 높을수록 이 사이에 다시 가득 채운다(10-23 실측 7v1)
    private readonly Dictionary<int, float> _lastBloom = new();

    public GEvoBlazingNoonEffect() : base("evo_gawain_combo") { }
    protected override void Bind()   { Hub.Hit += OnHubHit; RelicEvolutionNotice.Show(EffectKey); }
    protected override void Unbind() { Hub.Hit -= OnHubHit; }

    private void OnHubHit(HitInfo h)
    {
        if (!Hub.IsNoon) return;
        var mb = AsMonster(h.Target);
        if (mb == null || mb.IsDead) return;
        Hub.AddSunmark(mb, 1, ignoreGap: true);
        var st = RelicMarkStatus.Of(mb.gameObject, false);
        if (st == null || st.Sunmark < Hub.SunmarkCap || mb.IsDead) return;
        int id = mb.GetInstanceID();
        if (_lastBloom.TryGetValue(id, out float at) && Time.time - at < BloomGap) return;
        _lastBloom[id] = Time.time;
        Hub.Bloom(mb, st.ConsumeSunmark(), GawainMemoryHub.BloomCause.Instant);
        RelicEvolutionCue.Proc(EffectKey, Player, GawainMemoryHub.GawainColor);
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
        RelicEvolutionCue.Proc(EffectKey, Player, GawainMemoryHub.GawainColor);
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
        RelicEvolutionCue.Proc(EffectKey, Player, GawainMemoryHub.GawainColor);
    }
}

// ── 랜슬롯 ───────────────────────────────────────────────

/// <summary>끝없는 광란(연격) — 광기를 1.5배로(행동마다 받은 광기의 절반을 더) · 광란 중 10타마다 광란 +1초.</summary>
public sealed class LEvoEndlessFrenzyEffect : LancelotFragment
{
    private const int ExtendEvery = 10;
    private const float ExtendSeconds = 1f;
    private const float GainBonus = 0.5f;   // 광기 획득 ×1.5
    private int _frenzyHits;
    private float _gainCarry;
    private LancelotMadnessRelic _bound;

    public LEvoEndlessFrenzyEffect() : base("evo_lancelot_combo") { }
    protected override void Bind()   { Hub.Hit += OnHubHit; BindRelic(); RelicEvolutionNotice.Show(EffectKey); }
    protected override void Unbind()
    {
        Hub.Hit -= OnHubHit;
        if (_bound != null) _bound.ActionStackGained -= OnActionGain;
        _bound = null;
    }

    private void BindRelic()
    {
        var relic = Hub.Relic;
        if (relic == null || relic == _bound) return;
        if (_bound != null) _bound.ActionStackGained -= OnActionGain;
        _bound = relic;
        _bound.ActionStackGained += OnActionGain;
    }

    private void OnHubHit(HitInfo h)
    {
        var relic = Hub.Relic;
        if (relic == null) return;
        BindRelic();   // 묶을 때 유물이 아직 없었으면 첫 적중에서 잡는다
        if (relic.IsFrenzy)
        {
            if (++_frenzyHits >= ExtendEvery)
            {
                _frenzyHits = 0;
                relic.Madness?.ExtendFrenzy(ExtendSeconds);
                RelicEvolutionCue.Proc(EffectKey, Player, LancelotMemoryHub.LancelotColor);   // 광란 +1초가 보이게(10-23 W4)
            }
            return;
        }
        _frenzyHits = 0;
    }

    // 광기는 행동 단위로 들어온다(10-19 ⑤) — 예전 「적중 2타마다 +1」은 대상 수 × 타 수로 불어났다. 받은 몫의 절반을 더 준다(소수는 이월).
    private void OnActionGain(int n)
    {
        if (_bound == null || _bound.IsFrenzy) return;
        _gainCarry += n * GainBonus;
        int add = (int)_gainCarry;
        if (add <= 0) return;
        _gainCarry -= add;
        _bound.AddStack(add);
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
        if (mb == null || mb.IsDead) return;
        Hub.AddBrand(mb, 2, overflowToGrudge: true, ignoreGap: true);
        RelicEvolutionCue.Proc(EffectKey, Player, LancelotMemoryHub.LancelotColor);
    }

    private void OnFinisher(Vector3 pos, Vector3 fwd)
    {
        var relic = Hub.Relic;
        if (relic == null || Player == null) return;
        float bonus = relic.LastFinisherDamage * CritShare;
        if (bonus <= 0f) return;
        var near = Near(pos, FinisherRadius, Owner, 16);
        foreach (var mb in near)
            CombatQuery.DealSynergyDamage(mb, bonus, Owner, 1f, isCrit: true);
        if (near.Count > 0) RelicEvolutionCue.Proc(EffectKey, Player, LancelotMemoryHub.LancelotColor);
    }
}

/// <summary>
/// 배신의 화살(사격) — 원거리 적중이 낙인을 새긴다(같은 적 0.3초에 한 번) · 낙인이 가득 찬 적은 모두 찢는다(같은 적 2초에 한 번).
/// 예전엔 새기기만 했는데 허브 기본(광기 20부터 적중 → 낙인)과 겹쳐 상한에 막혔다(10-23 실측 8v ×0.98).
/// 찢을 때 받는 피해 +20%도 걸었더니 ×1.26(4초) · ×1.22(2초)로 다른 진화 띠(×1.06~1.17) 위였다 — +20%는 심판 찢기의 몫으로 둔다.
/// </summary>
public sealed class LEvoTraitorArrowEffect : LancelotFragment
{
    private const float Gap = 0.3f;
    private const float TearGap = 2f;
    private readonly Dictionary<int, float> _last = new();
    private readonly Dictionary<int, float> _lastTear = new();

    public LEvoTraitorArrowEffect() : base("evo_lancelot_ranged") { }
    protected override void Bind()   { Hub.Hit += OnHubHit; RelicEvolutionNotice.Show(EffectKey); }
    protected override void Unbind() { Hub.Hit -= OnHubHit; }

    private void OnHubHit(HitInfo h)
    {
        if (!h.IsRanged) return;
        var mb = AsMonster(h.Target);
        if (mb == null || mb.IsDead) return;
        int id = mb.GetInstanceID();
        if (LancelotMemoryHub.BrandOf(mb) >= Hub.BrandCap && !(_lastTear.TryGetValue(id, out float tornAt) && Time.time - tornAt < TearGap))
        {
            _lastTear[id] = Time.time;
            if (Hub.Tear(mb, LancelotMemoryHub.TearCause.Fragment) > 0) RelicEvolutionCue.Proc(EffectKey, Player, LancelotMemoryHub.LancelotColor);
            return;
        }
        if (_last.TryGetValue(id, out float at) && Time.time - at < Gap) return;
        _last[id] = Time.time;
        Hub.AddBrand(mb, 1, overflowToGrudge: true, ignoreGap: true);
    }
}
