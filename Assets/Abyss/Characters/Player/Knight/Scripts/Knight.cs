/// <summary>
/// 기본 캐릭터 (Default).
/// 기존 Knight를 리워크 — 공통 FSM/라우팅을 사용하며 기본 캐릭터 특성을 등록.
/// </summary>
public class Knight : PlayerController
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
        // 기본 캐릭터 특성1: 카드 액티브 아이템 (스텁 — 액티브 아이템 시스템 구현 전)
        RegisterPassive(new DefaultCardItemPassive());
    }

    protected override void RouteInputsToLayers()
    {
        DefaultRouteInputsToLayers();
    }
}
