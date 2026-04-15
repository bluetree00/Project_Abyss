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
            _controller.SetMoveScale(1f);

            // 낙하 공격 중이면 ActPlungeState가 착지를 직접 처리
            // → JumpBlend 착지 애니 및 CancelActState 건너뜀
            if (!_controller.IsPlunging)
            {
                _controller.Anim.SetFloat("JumpValue", 2f);
            }

            _stateChanger.Change(LocoState.Idle);
        }
    }

    public void Exit()
    {
        _entered = false;

        // 낙하 공격 중 → ActPlungeState가 착지를 직접 처리
        // 그 외 모든 경우 (공중 공격 포함) → 착지 시 ActState 초기화
        if (!_controller.IsPlunging)
            _controller.CancelActState();

        _controller.SetMoveScale(1f);
        _controller.ConsumeEnterAirAsJump();
        _controller.SetJumping(false);
        _controller.Anim.SetFloat("JumpValue", 0f);
    }
}
