public class ActNoneState : ILayerState<ActState>
{
    private PlayerController _controller;
    private ILayerStateChanger<ActState> _stateChanger;

    public void Init(PlayerController controller, ILayerStateChanger<ActState> stateChanger)
    {
        _controller = controller;
        _stateChanger = stateChanger;
    }

    public void Enter()
    {
        // 공격/스킬 종료 후 자동으로 Locomotion Idle로 전환
        if (_controller.LocoSM.CurrentId != LocoState.Idle)
            _controller.LocoSM.Change(LocoState.Idle);

        _controller.isAttacking = false;
        _controller.nextComboQueued = false;
        _controller.comboWindowOpen = false;
    }

    public void Update() { }
    public void Exit() { }
}
