using UnityEngine;

/// <summary>
/// 랜슬롯 메커닉 — 광기(Madness). 기본 공격 적중마다 광기 스택 +1.
/// 스택 누적/감쇠/자동발동은 MadnessStack이, 적중 트리거는 이 패시브가 담당.
/// </summary>
public sealed class LancelotMadnessPassive : CharacterPassiveBase
{
    public override string         PassiveName => "광기";
    public override PassiveTrigger Trigger     => PassiveTrigger.OnAttackHit;

    public override bool CanApply(PlayerController ctrl, in PassiveContext ctx)
        => ctx.damage > 0f && ctrl.RelicBehavior is LancelotMadnessRelic;

    public override void Apply(PlayerController ctrl, in PassiveContext ctx)
    {
        if (ctrl.RelicBehavior is LancelotMadnessRelic lm) lm.AddStack();
    }
}
