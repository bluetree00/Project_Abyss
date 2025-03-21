public abstract class State<T>
{
    /// <summary>
    /// 해당 상태에서 입력이 차단되는지 여부
    /// </summary>
    public virtual bool BlocksInput => false;

    public abstract void Enter(T owner);
    public abstract void Execute(T owner);
    public abstract void Exit(T owner);
}
