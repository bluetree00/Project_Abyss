using UnityEngine;

public class LocoAirState : ILayerState<LocoState>
{
    private PlayerController _controller;
    private ILayerStateChanger<LocoState> _stateChanger;

    private bool _entered = false; // 중복 Enter 방지

    // 블렌드 트리 값 (0 = Start, 1 = Keep)
    private float _blendValue = 0f;
    private float _blendSpeed = 0.01f;

    public void Init(PlayerController controller, ILayerStateChanger<LocoState> stateChanger)
    {
        _controller = controller;
        _stateChanger = stateChanger;
    }

    public void Enter()
    {
        _controller.SetMoveScale(0f);
        _controller.Anim.CrossFade("JumpBlend", 0.1f);

        _controller.SetJumping(true);
        _blendValue = _controller.EnterAirAsJump ? 0f : 1f;
        Debug.Log($"LocoAirState Enter: EnterAirAsJump={_controller.EnterAirAsJump}, isJumping={_controller.isJumping}");

        _controller.Anim.SetFloat("JumpValue", _blendValue);
    }


    public void Update()
    {
        // 착지 체크
        if (_controller.IsGrounded())
        {
            _controller.Anim.SetFloat("JumpValue", 2f);
            _controller.Anim.SetFloat("AirLightAttackValue", 2f);
            _controller.SetMoveScale(1f);
            _stateChanger.Change(LocoState.Idle);
        }
    }

    public void Exit()
    {
        _entered = false; // Exit 시 다시 Enter 가능
        _controller.SetMoveScale(1f);
        _controller.ConsumeEnterAirAsJump();
        _controller.SetJumping(false);
        _controller.Anim.SetFloat("JumpValue", 0f);
        _controller.Anim.SetFloat("AirLightAttackValue", 0f);
    }
}
