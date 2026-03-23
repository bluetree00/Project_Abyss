/// <summary>
/// FSM 특수 상태(LeeSpecialStateBase)를 래핑하는 BT Action 리프 노드.
///
/// 첫 Tick: ctx.Monster.ChangeState(state) 로 FSM 진입 → Running 반환
/// 이후 Tick: IsInSpecialState 가 true 면 Running, false 가 되면 Success 반환
/// </summary>
public class LeeBTFSMActionNode : LeeBTNode
{
    private readonly LeeSpecialStateBase _state;
    private bool _isActive;

    public LeeBTFSMActionNode(LeeSpecialStateBase state) => _state = state;

    public override LeeBTStatus Tick(LeeMonsterContext ctx)
    {
        if (!_isActive)
        {
            // 다른 패턴이 실행 중이면 진입하지 않는다
            if (ctx.Monster.IsInSpecialState)
                return LeeBTStatus.Failure;

            ctx.Monster.ChangeState(_state);
            _isActive = true;
            return LeeBTStatus.Running;
        }

        if (ctx.Monster.IsInSpecialState)
            return LeeBTStatus.Running;

        _isActive = false;
        return LeeBTStatus.Success;
    }

    public override void Reset() => _isActive = false;
}
