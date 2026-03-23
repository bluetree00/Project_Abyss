/// <summary>
/// Memory Sequence 복합 노드.
/// Running 상태인 자식 인덱스를 기억해 다음 프레임에 그 자식부터 재개한다.
/// 앞선 조건 노드를 재평가하지 않으므로 액션이 완료될 때까지 유지된다.
/// </summary>
public class LeeBTMemSequence : LeeBTNode
{
    private readonly LeeBTNode[] _children;
    private int _runningIndex;

    public LeeBTMemSequence(params LeeBTNode[] children) => _children = children;

    public override LeeBTStatus Tick(LeeMonsterContext ctx)
    {
        for (int i = _runningIndex; i < _children.Length; i++)
        {
            var status = _children[i].Tick(ctx);

            if (status == LeeBTStatus.Running)
            {
                _runningIndex = i;
                return LeeBTStatus.Running;
            }

            if (status == LeeBTStatus.Failure)
            {
                _runningIndex = 0;
                return LeeBTStatus.Failure;
            }
            // Success → 다음 자식으로
        }

        _runningIndex = 0;
        return LeeBTStatus.Success;
    }

    public override void Reset()
    {
        _runningIndex = 0;
        foreach (var child in _children)
            child.Reset();
    }
}
