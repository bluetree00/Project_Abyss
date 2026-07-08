using UnityEngine;

/// <summary>
/// 가웨인 — 태양의 열기(화상 부착). 여명(DawnMode)/정오(FlameMode) 구간 중 공격 적중 시 태양 화상 부여.
/// 정오는 강화(더 강한 DPS·긴 지속). 죽은 구간 없이 매 구간 능동 플레이의 핵심.
/// </summary>
public sealed class GawainSolarBurnPassive : CharacterPassiveBase
{
    private const float BurnTickInterval = 0.5f;

    public override string         PassiveName => "태양의 열기";
    public override PassiveTrigger Trigger     => PassiveTrigger.OnAttackHit;

    public override bool CanApply(PlayerController ctrl, in PassiveContext ctx)
        => ctx.target != null && ctx.damage > 0f
           && ctrl.RelicBehavior is GawainZenithRelic g && (g.DawnMode || g.FlameMode);

    public override void Apply(PlayerController ctrl, in PassiveContext ctx)
    {
        if (ctrl.RelicBehavior is not GawainZenithRelic g) return;
        float effAtk = ctrl.RuntimeStats.GetEffectiveAttack(AttackStatKind.Melee);
        float dps = effAtk * (g.FlameMode ? 0.15f : 0.10f);
        float dur = g.FlameMode ? 6f : 4f;
        MonsterBurnHandler.Apply(ctx.target, dps, dur, BurnTickInterval, ctrl.gameObject);
    }
}
