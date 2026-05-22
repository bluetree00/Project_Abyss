using UnityEngine;

public class LocoAirState : ILayerState<LocoState>
{
    private enum AirPhase { Start, Loop, Landing }

    private const float LandingDuration = 0.15f;

    private PlayerController _controller;
    private ILayerStateChanger<LocoState> _stateChanger;

    private AirPhase _phase;
    private float _landingTimer;

    public void Init(PlayerController controller, ILayerStateChanger<LocoState> stateChanger)
    {
        _controller = controller;
        _stateChanger = stateChanger;
    }

    public void Enter()
    {
        _phase = AirPhase.Start;
        _landingTimer = 0f;

        // ProcessJump에서 이미 CrossFade 했으므로,
        // 낙하 진입(점프 없이 떨어진 경우)만 여기서 처리
        if (!_controller.IsJumping)
        {
            _phase = AirPhase.Loop;
            _controller.Anim.SetFloat("JumpValue", 1f);
            _controller.Anim.CrossFade("JumpBlend", 0.1f);
        }
    }

    public void Update()
    {
        switch (_phase)
        {
            case AirPhase.Start:
                if (_controller.Rigid.linearVelocity.y <= 0f)
                {
                    _phase = AirPhase.Loop;
                    _controller.Anim.SetFloat("JumpValue", 1f);
                }
                break;

            case AirPhase.Loop:
                if (_controller.IsGrounded())
                {
                    EnterLanding();
                }
                break;

            case AirPhase.Landing:
                // 착지 중 재점프 → 즉시 Start 단계로 리셋
                if (_controller.IsJumping)
                {
                    _controller.IsLanding = false;
                    _phase = AirPhase.Start;
                    break;
                }

                _landingTimer -= Time.deltaTime;
                if (_landingTimer <= 0f)
                {
                    float speed = _controller.MoveDirection.magnitude * _controller.MoveScale;
                    _stateChanger.Change(speed > 0.05f ? LocoState.Move : LocoState.Idle);
                }
                break;
        }
    }

    public void Exit()
    {
        _controller.IsLanding = false;
    }

    private void EnterLanding()
    {
        _phase = AirPhase.Landing;
        _landingTimer = LandingDuration;

        _controller.IsLanding = true;

        // 공중 공격 중이든 아니든, 착지 애니메이션 강제 재생
        _controller.Anim.CrossFade("JumpLand", 0.05f);

        // 아이템 효과: 점프 착지 hook
        var mgr = GameRunBootstrapper.Instance?.Run?.EffectManager;
        mgr?.OnJumpLand(_controller.transform.position);
    }
}