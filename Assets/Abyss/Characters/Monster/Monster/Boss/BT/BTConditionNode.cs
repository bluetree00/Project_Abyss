namespace Abyss.Monster
{
    /// <summary>
    /// Condition 리프 노드.
    /// 람다 조건이 true 면 Success, false 면 Failure 반환.
    /// </summary>
    public class BTConditionNode : BTNode
    {
        private readonly System.Func<MonsterContext, bool> _predicate;

        public BTConditionNode(System.Func<MonsterContext, bool> predicate)
            => _predicate = predicate;

        public override BTStatus Tick(MonsterContext ctx)
            => _predicate(ctx) ? BTStatus.Success : BTStatus.Failure;
    }
}
