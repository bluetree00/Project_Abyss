using UnityEngine;

/// <summary>
/// 랜슬롯 — 광란의 여운(흡혈). 광란(Frenzy) 상태 중 공격 적중 시 가한 피해의 일부를 회복.
/// 빈틈(배신의 대가) 대체 — 폭발 후 위기가 아니라 '폭발 후 전진(회복)'.
/// </summary>
public sealed class LancelotFrenzyLifestealPassive : CharacterPassiveBase
{
    public override string         PassiveName => "광란의 여운";
    public override PassiveTrigger Trigger     => PassiveTrigger.OnAttackHit;

    public override bool CanApply(PlayerController ctrl, in PassiveContext ctx)
        => ctx.damage > 0f && ctrl.RelicBehavior is LancelotMadnessRelic lm && lm.IsFrenzy;

    public override void Apply(PlayerController ctrl, in PassiveContext ctx)
    {
        if (ctrl.RelicBehavior is LancelotMadnessRelic lm)
        {
            int heal = Mathf.RoundToInt(ctx.damage * lm.LifestealPct);
            if (heal > 0) ctrl.Heal(heal);
        }
    }
}
