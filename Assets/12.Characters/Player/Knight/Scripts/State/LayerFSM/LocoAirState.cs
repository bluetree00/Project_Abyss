// LocoAirState.cs
using UnityEngine;

public class LocoAirState : ILayerState<LocoState>
{
    private PlayerController _controller;
    private ILayerStateChanger<LocoState> _stateChanger;

    public void Init(PlayerController controller, ILayerStateChanger<LocoState> stateChanger)
    { _controller = controller; _stateChanger = stateChanger; }

    public void Enter() { /* 점프/낙하 진입 처리 필요 시 */ }

    public void Update()
    {
        var dir = _controller.IsMoveLocked ? Vector3.zero
                                           : _controller.MoveDirection * _controller.MoveScale;
        _controller.MoveAbility?.Move(_controller, dir);

        if (_controller.IsGrounded())
        {
            var next = (_controller.MoveDirection.sqrMagnitude > 0.0001f)
                ? LocoState.Move : LocoState.Idle;
            _stateChanger.Change(next);
        }
    }

    public void Exit() { /* 착지 처리 필요 시 */ }
}
