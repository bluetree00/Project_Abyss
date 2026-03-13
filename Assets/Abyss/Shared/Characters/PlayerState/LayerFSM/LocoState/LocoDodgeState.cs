using UnityEngine;

public class LocoDodgeState : ILayerState<LocoState>
{
    private PlayerController _controller;
    private ILayerStateChanger<LocoState> _stateChanger;

    private Vector3 _dodgeDir;
    private float _moveEndTime;

    public void Init(PlayerController controller, ILayerStateChanger<LocoState> stateChanger)
    { _controller = controller; _stateChanger = stateChanger; }

    public void Enter()
    {
        _controller.RotateTowardsInput();
        _dodgeDir = _controller.transform.forward;
        _moveEndTime = Time.time + _controller.CharacterData.dashDuration;

        _controller.Anim.CrossFade("Dodge", 0.05f);
        _controller.SetMoveScale(0f);
    }

    public void Update()
    {
        if (Time.time < _moveEndTime)
        {
            // 수평 이동. y는 중력/점프 유지
            float spd = _controller.CharacterData.dashSpeed;
            float vy = _controller.Rigid.linearVelocity.y;
            _controller.Rigid.linearVelocity = new Vector3(
                _dodgeDir.x * spd,
                vy,
                _dodgeDir.z * spd
            );
        }
        else
        {
            // 수평 속도 정지 후 다음 상태로
            float vy = _controller.Rigid.linearVelocity.y;
            _controller.Rigid.linearVelocity = new Vector3(0f, vy, 0f);

            _controller.SetMoveScale(1f);
            var next = !_controller.IsGrounded() ? LocoState.Air
                       : (_controller.MoveDirection.sqrMagnitude > 0.0001f ? LocoState.Move
                                                                           : LocoState.Idle);
            _stateChanger.Change(next);
        }
    }

    public void Exit()
    {
        _controller.DodgeCooldownEnd = Time.time + _controller.CharacterData.dodgeCooldown;
        _controller.SetMoveScale(1f);
    }
}
