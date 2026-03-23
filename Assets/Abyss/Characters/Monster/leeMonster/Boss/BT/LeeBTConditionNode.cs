/// <summary>
/// Condition 리프 노드.
/// 람다 조건이 true 면 Success, false 면 Failure 반환.
/// </summary>
public class LeeBTConditionNode : LeeBTNode
{
    private readonly System.Func<LeeMonsterContext, bool> _predicate;

    public LeeBTConditionNode(System.Func<LeeMonsterContext, bool> predicate)
        => _predicate = predicate;

    public override LeeBTStatus Tick(LeeMonsterContext ctx)
        => _predicate(ctx) ? LeeBTStatus.Success : LeeBTStatus.Failure;
}
