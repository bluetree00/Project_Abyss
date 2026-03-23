/// <summary>
/// Priority Selector 복합 노드.
/// 자식을 순서대로 Tick해 Failure 가 아닌 첫 결과를 반환한다.
/// </summary>
public class LeeBTSelector : LeeBTNode
{
    private readonly LeeBTNode[] _children;

    public LeeBTSelector(params LeeBTNode[] children) => _children = children;

    public override LeeBTStatus Tick(LeeMonsterContext ctx)
    {
        foreach (var child in _children)
        {
            var status = child.Tick(ctx);
            if (status != LeeBTStatus.Failure)
                return status;
        }
        return LeeBTStatus.Failure;
    }

    public override void Reset()
    {
        foreach (var child in _children)
            child.Reset();
    }
}
