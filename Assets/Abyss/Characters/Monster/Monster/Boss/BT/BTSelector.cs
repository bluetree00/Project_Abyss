namespace Abyss.Monster
{
    /// <summary>
    /// Priority Selector 복합 노드.
    /// 자식을 순서대로 Tick해 Failure 가 아닌 첫 결과를 반환한다.
    /// </summary>
    public class BTSelector : BTNode
    {
        private readonly BTNode[] _children;

        public BTSelector(params BTNode[] children) => _children = children;

        public override BTStatus Tick(MonsterContext ctx)
        {
            foreach (var child in _children)
            {
                var status = child.Tick(ctx);
                if (status != BTStatus.Failure)
                    return status;
            }
            return BTStatus.Failure;
        }

        public override void Reset()
        {
            foreach (var child in _children)
                child.Reset();
        }
    }
}
