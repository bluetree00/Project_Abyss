using UnityEngine;

/// <summary>
/// 광전사 특성1: 공격 시 피해 흡수 10%
/// </summary>
public class BerserkerLifestealPassive : CharacterPassiveBase
{
    private readonly float _lifestealPercent;

    public BerserkerLifestealPassive(float lifestealPercent = 0.10f)
        => _lifestealPercent = lifestealPercent;

    public override string         PassiveName => "피해 흡수";
    public override PassiveTrigger Trigger     => PassiveTrigger.OnAttackHit;

    public override void Apply(PlayerController ctrl, in PassiveContext ctx)
    {
        int healAmount = Mathf.Max(1, (int)(ctx.damage * _lifestealPercent));
        ctrl.Heal(healAmount);
    }
}
