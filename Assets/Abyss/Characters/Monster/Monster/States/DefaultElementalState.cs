namespace Abyss.Monster
{
/// <summary>
/// 공통 원소 상태 — 원소별 오버라이드 SO 가 없을 때 사용되는 기본 상태.
///
/// 현재 단계에서는 효과 미구현.
/// 추후 각 원소별 실제 효과(마비·화상·침수 등)로 교체 예정.
///
/// 오버라이드 방법: ElementalStateSO 파생 SO → Create() 에서 커스텀 상태 반환.
/// </summary>
public class DefaultElementalState : SpecialStateBase
{
    private readonly ElementType _element;

    public DefaultElementalState(ElementType element) { _element = element; }

    public override SpecialStateConstraint Constraints => SpecialStateConstraint.None;

    public override void Enter(MonsterContext ctx)
    {
        // TODO: 원소별 효과 구현 예정
        // _element 에 따라 마비(Lightning) / 침수(Water) / 화상(Fire) / 등 적용
    }

    public override void Update(MonsterContext ctx)
    {
        // 미구현 단계 — 즉시 이전 흐름으로 복귀
        ctx.Monster.ChangeState<ChaseState>();
    }

    public override void Exit(MonsterContext ctx) { }
}
}
