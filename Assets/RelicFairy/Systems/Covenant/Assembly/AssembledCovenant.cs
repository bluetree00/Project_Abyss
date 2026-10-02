using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 조립 서약 — 원인(Cause) × 효과(Effect) 데이터구동 단일 클래스(조합마다 클래스 X).
/// CovenantId = "asm:&lt;causeId&gt;@&lt;tier&gt;|&lt;effectId&gt;@&lt;tier&gt;" 로 인코딩 → 기존 CovenantFactory/Handler/세이브 그대로 재사용.
/// (@tier 생략 시 실버로 폴백 — 구버전 id 호환)
/// 원인 상태기계는 <see cref="CovenantCauseTracker"/>, 효과 실행은 <see cref="CovenantClause"/> 한 개가 맡는다
/// (10-02 「한 장의 서약서」 E1 — 서약서(문장)가 같은 둘을 쓴다). 효과 크기는 <see cref="CovenantMath"/>가 단독으로 계산한다
/// (표시=동작 단일 소스). 티어 = 순수 파워 축.
/// </summary>
public sealed class AssembledCovenant : CovenantBase, ICovenantClauseHost
{
    public const string Prefix = "asm:";

    private readonly string _causeId, _effectId;
    private readonly CovenantTier _causeTier, _effectTier;
    private readonly bool   _resolved;
    private readonly CovenantPalette.CauseDef  _cause;
    private readonly CovenantPalette.EffectDef _effect;

    private readonly CovenantCauseTracker _tracker;
    private readonly CovenantClause       _clause;

    /// <summary>
    /// 효과 적용 중 플래그. 광역 폭발이 적을 죽이면 그 처치가 다시 원인(학살·사냥 개시)을 물고
    /// 같은 효과를 재입력한다 — 한 번의 발동이 프레임 안에서 스스로를 되먹여 폭주한다.
    /// 적용 구간에 들어온 트리거는 통째로 무시해 되먹임 고리를 끊는다.
    /// </summary>
    private bool _inEffect;

    private string _effectText;   // 효과 수치 한 줄(표시용 캐시 — 원인·효과·등급이 정해지면 바뀌지 않는다)

    /// <param name="causePart">"causeId" 또는 "causeId@tier"</param>
    /// <param name="effectPart">"effectId" 또는 "effectId@tier"</param>
    public AssembledCovenant(string causePart, string effectPart)
    {
        (_causeId,  _causeTier)  = SplitTier(causePart);
        (_effectId, _effectTier) = SplitTier(effectPart);
        bool a = CovenantPalette.TryGetCause(_causeId, out _cause);
        bool b = CovenantPalette.TryGetEffect(_effectId, out _effect);
        _resolved = a && b;
        if (!_resolved) return;

        _clause  = new CovenantClause(this, _effectId, _effectTier, _effect, _cause, _causeTier);
        _tracker = new CovenantCauseTracker(_cause, this, ApplyEffect, _clause.OnProximitySample);
    }

    /// <summary>팔레트에서 원인·효과가 모두 해석됐는지. 미해결이면 껍데기라 슬롯을 차지시키지 않는다.</summary>
    public bool Resolved => _resolved;

    // ── 상태 통화 노출(시너지 힌트) ──────────────────────
    // UI가 "지금 가진 서약이 이 조합과 물리는가"를 판정하려면 보유 서약 쪽 통화·역할을 읽을 수 있어야 한다.
    /// <summary>이 서약이 다루는 상태 통화.</summary>
    public StatusCurrency Status => _resolved ? _effect.status : StatusCurrency.None;
    /// <summary>그 통화를 거는가(Apply) 먹는가(Consume).</summary>
    public StatusRole Role => _resolved ? _effect.role : StatusRole.None;
    /// <summary>효과 표시명(힌트 문구가 "무엇과 물리는지" 이름으로 짚어준다).</summary>
    public string EffectName => _resolved ? _effect.name : null;

    private static (string id, CovenantTier tier) SplitTier(string part)
    {
        if (string.IsNullOrEmpty(part)) return (part, CovenantTier.Silver);
        int at = part.IndexOf('@');
        return at < 0
            ? (part, CovenantTier.Silver)
            : (part.Substring(0, at), CovenantTierUtil.Parse(part.Substring(at + 1)));
    }

    public static string MakeId(string causeId, CovenantTier causeTier, string effectId, CovenantTier effectTier)
        => Prefix + causeId + "@" + causeTier.Code() + "|" + effectId + "@" + effectTier.Code();

    public override string CovenantId       => MakeId(_causeId, _causeTier, _effectId, _effectTier);
    public override CovenantCategory Category => _resolved ? _cause.category : base.Category;
    public override string DisplayName      => _resolved
        ? _cause.name + "[" + _causeTier.DisplayName() + "] × " + _effect.name + "[" + _effectTier.DisplayName() + "]"
        : CovenantId;
    public override string BasicDescription => _resolved ? _cause.desc + " → " + EffectText : string.Empty;

    // 조립에 쓰인 부품 id. CovenantHandler가 봉인된 짝(IsBannedPair)을 서비스단에서 거르는 데 쓴다 —
    // 조립 화면의 버튼 잠금만으로는 UI 밖 진입점이 하나라도 생기는 순간 뚫린다.
    public string CauseId  => _causeId;
    public string EffectId => _effectId;

    // 원인/결과를 따로 노출 → HUD가 한 줄로 이어붙이지 않고 줄을 나눠 보여준다.
    public override string CauseText  => _resolved ? _cause.desc  : null;
    // 효과는 서술(「상처가 벌어져 계속 덧난다」)이 아니라 실제 수치로 — 조립 화면과 같은 계산(CovenantMath)을 탄다(09-29).
    public override string EffectText => _resolved
        ? _effectText ??= CovenantAssemblePreview.Build(_causeId, _causeTier, _effectId, _effectTier).EffectAmountCompact()
        : null;

    // ── 원인 트리거 → ApplyEffect ─────────────────────────
    public override void OnAttackHit(GameObject target, float dmg)
    {
        if (!_resolved || _inEffect) return;
        _tracker.OnAttackHit(target);
    }

    public override void OnKill(GameObject target)
    {
        if (!_resolved || _inEffect) return;
        _tracker.OnKill(target);
    }

    public override void OnWeaponSwap(WeaponData prev, WeaponData next)
    {
        if (_resolved && !_inEffect) _tracker.OnWeaponSwap();
    }

    public override void OnRoomEnter()
    {
        if (_resolved) _tracker.OnRoomEnter();
    }

    public override void OnRoomClear()
    {
        if (_resolved && !_inEffect) _tracker.OnRoomClear();
    }

    public override void OnSkillUse(SkillType skill)
    {
        if (_resolved && !_inEffect) _tracker.OnSkillUse();
    }

    public override void Tick(float deltaTime)
    {
        if (!_resolved || _inEffect) return;
        _tracker.Tick(deltaTime);
        _clause.Tick();
    }

    // ── 피해 · 스탯 · 사망 방지 · 버프창 → 절 ───────────────
    public override void ModifyOutgoingDamage(ref float damage, CombatContext ctx)
    {
        if (_resolved) _clause.ModifyOutgoingDamage(ref damage);
    }

    public override void ModifyIncomingDamage(ref float damage, CombatContext ctx)
    {
        if (_resolved) _clause.ModifyIncomingDamage(ref damage);
    }

    public override IEnumerable<StatModifier> GetStatModifiers()
        => _resolved ? _clause.GetStatModifiers() : Array.Empty<StatModifier>();

    public override bool TryPreventDeath() => _resolved && _clause.TryPreventDeath();

    public override bool TryGetBuffView(out BuffViewItem item)
    {
        if (_resolved) return _clause.TryGetBuffView(out item);
        item = default;
        return false;
    }

    // ── 효과 적용 ─────────────────────────────────────────
    private void ApplyEffect(GameObject target)
    {
        if (Ctx == null || _inEffect) return;

        _inEffect = true;
        try
        {
            _clause.TryFire(target);
        }
        finally
        {
            _inEffect = false;
        }
    }

    // ── ICovenantClauseHost ───────────────────────────────
    CovenantContext ICovenantClauseHost.Context => Ctx;
    Vector3 ICovenantClauseHost.PlayerPosition  => PlayerPos;
    void ICovenantClauseHost.RequestStatRefresh() => RefreshStats();
    int ICovenantClauseHost.DealAoe(Vector3 center, float radius, float multiplier) => DealAoe(center, radius, multiplier, 0.3f);
}
