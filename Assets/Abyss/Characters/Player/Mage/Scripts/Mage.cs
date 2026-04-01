/// <summary>
/// 마법사 (Mage).
/// 마법 무기 장착 클리어 시 해금. 높은 원거리 공격력, 쿨감 특성.
/// 특성1 (스킬 쿨타임 -20%)과 특성2 (액티브 아이템 쿨타임 -30%)는
/// PassiveSO의 baseModifiers로 처리 — 코드 패시브 불필요.
/// </summary>
public class Mage : PlayerController
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
        // 마법사 특성은 모두 PassiveSO.baseModifiers 기반
        // (SkillCooldownReduction: 0.2, ActiveItemCooldownReduction: 0.3)
        // → PlayerRuntimeStats.ApplyPassive()에서 자동 처리
    }

    protected override void RouteInputsToLayers()
    {
        DefaultRouteInputsToLayers();
    }
}
