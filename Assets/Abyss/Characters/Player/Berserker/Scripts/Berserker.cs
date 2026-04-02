/// <summary>
/// 광전사 (Berserker).
/// 2회 클리어 시 해금. 높은 체력/근접 공격력, 흡혈 + 공속 스택 특성.
/// </summary>
public class Berserker : PlayerController
{
    protected override void InitLayerFSMs()
    {
        RegisterDefaultFSMs();
    }

    protected override bool IsInAttackOrSkillState() =>
        base.IsInAttackOrSkillState()           ||
        actSM.CurrentId == ActState.Charge      ||
        actSM.CurrentId == ActState.HeavyAttack;

    protected override void InitPassives()
    {
        RegisterPassive(new BerserkerLifestealPassive(lifestealPercent: 0.10f));
        RegisterPassive(new BerserkerFrenzyPassive(
            attackSpeedPerStack: 0.05f, maxStacks: 5, stackDuration: 1f));
    }

    protected override void RouteInputsToLayers()
    {
        DefaultRouteInputsToLayers();
    }
}
