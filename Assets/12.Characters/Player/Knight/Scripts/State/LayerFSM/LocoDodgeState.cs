// LocoDodgeState.cs
using UnityEngine;

public class LocoDodgeState : ILayerState<LocoState>
{
    private PlayerController _controller;
    private ILayerStateChanger<LocoState> _stateChanger;

    private float _time;
    private float _duration = 0.35f; // 회피 무적/모션 시간

    public void Init(PlayerController controller, ILayerStateChanger<LocoState> stateChanger)
    { _controller = controller; _stateChanger = stateChanger; }

    public void Enter()
    {
        _time = 0f;
        _controller.DodgeAbility?.Dodge(_controller);
        _controller.AcquireMoveLock();
        _controller.SetMoveScale(0f);
    }

    public void Update()
    {
        _time += Time.deltaTime;
        if (_time >= _duration)
        {
            _controller.ReleaseMoveLock();
            _controller.SetMoveScale(1f);
            var next = !_controller.IsGrounded() ? LocoState.Air
                       : (_controller.MoveDirection.sqrMagnitude > 0.0001f ? LocoState.Move
                                                                          : LocoState.Idle);
            _stateChanger.Change(next);
        }
    }

    public void Exit() { }
}
