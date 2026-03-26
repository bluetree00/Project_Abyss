using Abyss.Monster;
using UnityEngine;

/// <summary>BlackKnight 블랙보드의 특정 쿨다운이 0 이하인지 확인하는 조건.</summary>
[CreateAssetMenu(fileName = "BK_Cond_Cooldown",
                 menuName  = "Abyss/Boss/BlackKnight/Conditions/Cooldown")]
public class BKCooldownConditionSO : BossConditionSO
{
    public BKCooldownType cooldownType;

    public override bool Evaluate(BossPatternContext ctx)
    {
        var bb = ctx.Blackboard;
        return cooldownType switch
        {
            BKCooldownType.Charge    => bb.ChargeCooldown    <= 0f,
            BKCooldownType.Overhead  => bb.OverheadCooldown  <= 0f,
            BKCooldownType.Rain      => bb.RainCooldown      <= 0f,
            BKCooldownType.Scatter   => bb.ScatterCooldown   <= 0f,
            BKCooldownType.Leap      => bb.LeapCooldown      <= 0f,
            BKCooldownType.Backstep  => bb.BackstepCooldown  <= 0f,
            BKCooldownType.DashSlash => bb.DashSlashCooldown <= 0f,
            BKCooldownType.Pressure  => bb.PressureCooldown  <= 0f,
            _                        => false,
        };
    }
}

public enum BKCooldownType
{
    Charge,
    Overhead,
    Rain,
    Scatter,
    Leap,
    Backstep,
    DashSlash,
    Pressure,
}
