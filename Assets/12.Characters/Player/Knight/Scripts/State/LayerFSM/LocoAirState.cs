using UnityEngine;

public class LocoAirState : ILayerState<LocoState>
{
    private PlayerController _controller;
    private ILayerStateChanger<LocoState> _stateChanger;

    // 블렌드 트리 값 (0 = Start, 1 = Keep)
    private float _blendValue = 1f;
    private float _blendSpeed = 0.1f; // 1초에 3만큼 증가, 필요에 따라 조절

    public void Init(PlayerController controller, ILayerStateChanger<LocoState> stateChanger)
    {
        _controller = controller;
        _stateChanger = stateChanger;
    }

   public void Enter()
    {
        _controller.SetMoveScale(0f); // 이동 제한

        _controller.Anim.CrossFade("JumpBlend", 0.1f);
        // 점프 입력으로 진입한 경우 Start 애니메이션부터 시작
        if (_controller.EnterAirAsJump)
        {
            _blendValue = 0f; // Start에서 시작
            _controller.ConsumeEnterAirAsJump();
        }
        else
        {
            // 점프가 아닌 공중 진입: Keep 상태로 바로 시작
            _blendValue = 1f;
        }

        // 초기 블렌드값 적용
        _controller.Anim.SetFloat("JumpValue", _blendValue);
    }

    public void Update()
    {
        // 블렌드 트리 값 업데이트 (Start → Keep)
        if (_blendValue < 1f)
        {
            _blendValue += Time.deltaTime * _blendSpeed;
            _blendValue = Mathf.Min(_blendValue, 1f);
            _controller.Anim.SetFloat("JumpValue", _blendValue);
        }

        // 착지 체크
        if (_controller.IsGrounded())
        {
            _stateChanger.Change(LocoState.Idle); // 착지 후 Idle 전환
        }
    }

    public void Exit()
    {
        // 공중 상태 종료 시 블렌드 초기화 (선택)
          _controller.SetMoveScale(1f); // 이동 제한 해제
        _controller.Anim.SetFloat("JumpValue", 0f);
    }

}
