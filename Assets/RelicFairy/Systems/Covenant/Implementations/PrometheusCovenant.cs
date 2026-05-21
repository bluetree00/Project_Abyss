using UnityEngine;

/// <summary>
/// 프로메테우스의 서약 — 훔친 불꽃의 대가로 맺어진 희생 계약
///
/// [Basic]    스킬 쿨타임 -40%, 사용마다 최대 체력 2% 소모
/// [Enhanced] 쿨타임 -55%, 소모 1.5%
/// [Evolved]  체력 소모량이 즉시 다음 스킬의 추가 피해로 전환
/// </summary>
public sealed class PrometheusCovenant : CovenantBase
{
    private const int V_COOLDOWN_REDUCTION = 0;
    private const int V_HP_COST_RATIO      = 1;

    public override string CovenantId => CovenantFactory.Prometheus;

    // ── 표시 데이터 ──────────────────────────────────────
    public override string DisplayName         => "프로메테우스의 서약";
    public override string LoreText            => "훔친 불꽃의 대가로 맺어진 희생 계약";
    public override string BasicDescription    => "스킬 쿨타임 -40%, 사용마다 최대 체력 2% 소모";
    public override string EnhancedDescription => "쿨타임 -55%, 소모 1.5%";
    public override string EvolvedDescription  => "체력 소모량이 즉시 다음 스킬의 추가 피해로 전환";

    // ── 런타임 상태 ──────────────────────────────────────
    private float _pendingBurstDamage;

    private float CooldownReduction => V(V_COOLDOWN_REDUCTION, 0.40f);
    private float HpCostRatio       => V(V_HP_COST_RATIO,      0.02f);
    private bool  IsEvolved         => Stage == CovenantStage.Evolved;

    // ── 메커닉 수정 ──────────────────────────────────────
    public override bool OverrideSkillCost(SkillType skill, ref SkillCostContext ctx)
    {
        ctx.HpCostRatio      = HpCostRatio;
        ctx.CooldownOverride = -(CooldownReduction);
        return true;
    }

    // ── 이벤트 ──────────────────────────────────────────
    public override void OnSkillUse(SkillType skill)
    {
        int hpCost = Mathf.RoundToInt(Ctx.RunState.MaxHp * HpCostRatio);
        Ctx.RunState.Damage(hpCost);

        if (IsEvolved)
            _pendingBurstDamage += hpCost;
    }

    // ── 피해 파이프라인 ──────────────────────────────────
    public override void ModifyOutgoingDamage(ref float damage, CombatContext ctx)
    {
        if (!IsEvolved || _pendingBurstDamage <= 0f || !ctx.IsSkillDamage) return;

        damage             += _pendingBurstDamage;
        _pendingBurstDamage = 0f;
    }
}
