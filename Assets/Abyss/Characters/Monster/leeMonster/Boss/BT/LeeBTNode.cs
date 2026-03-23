/// <summary>
/// Lee 몬스터 BT 노드 추상 기반 클래스.
/// LeeMonsterContext 를 매 프레임 받아 실행 결과를 반환한다.
/// </summary>
public abstract class LeeBTNode
{
    public abstract LeeBTStatus Tick(LeeMonsterContext ctx);

    public virtual void Reset() { }
}
