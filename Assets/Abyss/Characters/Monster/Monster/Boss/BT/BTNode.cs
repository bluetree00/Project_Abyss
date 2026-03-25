namespace Abyss.Monster
{
    /// <summary>
    /// 몬스터 BT 노드 추상 기반 클래스.
    /// MonsterContext 를 매 프레임 받아 실행 결과를 반환한다.
    /// </summary>
    public abstract class BTNode
    {
        public abstract BTStatus Tick(MonsterContext ctx);

        public virtual void Reset() { }
    }
}
