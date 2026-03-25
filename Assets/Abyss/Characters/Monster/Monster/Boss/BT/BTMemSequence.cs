namespace Abyss.Monster
{
    /// <summary>
    /// Memory Sequence 복합 노드.
    /// Running 상태인 자식 인덱스를 기억해 다음 프레임에 그 자식부터 재개한다.
    /// 앞선 조건 노드를 재평가하지 않으므로 액션이 완료될 때까지 유지된다.
    /// </summary>
    public class BTMemSequence : BTNode
    {
        private readonly BTNode[] _children;
        private int _runningIndex;

        public BTMemSequence(params BTNode[] children) => _children = children;

        public override BTStatus Tick(MonsterContext ctx)
        {
            for (int i = _runningIndex; i < _children.Length; i++)
            {
                var status = _children[i].Tick(ctx);

                if (status == BTStatus.Running)
                {
                    _runningIndex = i;
                    return BTStatus.Running;
                }

                if (status == BTStatus.Failure)
                {
                    _runningIndex = 0;
                    return BTStatus.Failure;
                }
                // Success → 다음 자식으로
            }

            _runningIndex = 0;
            return BTStatus.Success;
        }

        public override void Reset()
        {
            _runningIndex = 0;
            foreach (var child in _children)
                child.Reset();
        }
    }
}
