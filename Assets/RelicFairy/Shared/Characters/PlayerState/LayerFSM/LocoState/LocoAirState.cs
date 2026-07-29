using UnityEngine;

public class LocoAirState : ILayerState<LocoState>
{
    // MinorDrop: 점프 없이 떨어졌고 낙하 높이가 임계 미만 — 추락/착지 애니 생략, 로코모션 유지.
    private enum AirPhase { Start, MinorDrop, Loop, Landing }

    private const float LandingDuration = 0.15f;

    // CharacterData.minFallAnimHeight 미설정 시 사용할 기본 임계 높이(m).
    private const float DefaultMinFallAnimHeight = 0.6f;

    // 착지 충격 카메라 셰이크 — 낙하 속도(m/s) 이 범위로 강도 매핑. Min 미만은 셰이크 없음(평범한 점프 착지).
    private const float LandShakeMinSpeed = 6f;
    private const float LandShakeMaxSpeed = 18f;

    private PlayerController _controller;
    private ILayerStateChanger<LocoState> _stateChanger;

    private AirPhase _phase;
    private float _landingTimer;
    private float _takeoffY;     // 낙하 시작 시점의 Y (누적 낙하 높이 안전망용)
    private float _fallThreshold; // 이번 낙하의 추락 애니 임계 높이
    private float _maxFallSpeed;  // 체공 중 최대 하강 속도(착지 충격 셰이크 강도용)

    public void Init(PlayerController controller, ILayerStateChanger<LocoState> stateChanger)
    {
        _controller = controller;
        _stateChanger = stateChanger;
    }

    public void Enter()
    {
        _phase = AirPhase.Start;
        _landingTimer = 0f;
        _maxFallSpeed = 0f;

        // ProcessJump에서 이미 CrossFade 했으므로, 낙하 진입(점프 없이 떨어진 경우)만 여기서 처리.
        if (!_controller.IsJumping)
        {
            _takeoffY = _controller.transform.position.y;
            var cd = _controller.CharacterData;
            _fallThreshold = cd != null && cd.minFallAnimHeight > 0.01f ? cd.minFallAnimHeight : DefaultMinFallAnimHeight;

            // 발밑에 임계 높이 안쪽으로 지면이 있으면 작은 단차 → 추락/착지 애니 생략하고 로코모션 유지.
            if (HasGroundWithin(_fallThreshold))
            {
                _phase = AirPhase.MinorDrop;
            }
            else
            {
                // 실제 추락 — 추락 루프 애니 재생.
                _phase = AirPhase.Loop;
                _controller.Anim.SetFloat("JumpValue", 1f);
                _controller.Anim.CrossFade("JumpBlend", 0.1f);
            }
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

            case AirPhase.MinorDrop:
                // 재점프 → 일반 점프 흐름으로 복귀
                if (_controller.IsJumping)
                {
                    _phase = AirPhase.Start;
                    break;
                }
                // 작은 단차 착지 — 추락/착지 애니 없이 즉시 지상 상태로 복귀
                if (_controller.IsGrounded())
                {
                    float spd = _controller.MoveDirection.magnitude * _controller.MoveScale;
                    _stateChanger.Change(spd > 0.05f ? LocoState.Move : LocoState.Idle);
                    break;
                }
                // 안전망: 예측보다 더 떨어지면(임계 초과) 추락 애니로 전환
                if (_takeoffY - _controller.transform.position.y > _fallThreshold)
                {
                    _phase = AirPhase.Loop;
                    _controller.Anim.SetFloat("JumpValue", 1f);
                    _controller.Anim.CrossFade("JumpBlend", 0.1f);
                }
                break;

            case AirPhase.Loop:
                // 착지 충격 강도용: 체공 중 최대 하강 속도 추적.
                float fall = -_controller.Rigid.linearVelocity.y;
                if (fall > _maxFallSpeed) _maxFallSpeed = fall;
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

        // 착지 충격 — 낙하 속도 비례 카메라 셰이크(약한 착지는 스킵). 무게감/임팩트.
        float impact = Mathf.InverseLerp(LandShakeMinSpeed, LandShakeMaxSpeed, _maxFallSpeed);
        if (impact > 0f)
            HitFeelService.CameraShake(Mathf.Lerp(0.03f, 0.12f, impact), 0.14f);

        // 공중 공격 중이든 아니든, 착지 애니메이션 강제 재생
        _controller.Anim.CrossFade("JumpLand", 0.05f);

        // 아이템 효과: 점프 착지 hook
        var mgr = GameRunBootstrapper.Instance?.Run?.EffectManager;
        mgr?.OnJumpLand(_controller.transform.position);
    }

    // 발(약간 위)에서 아래로 height(m)만큼 지면이 있는지 예측. 있으면 작은 단차로 간주.
    private bool HasGroundWithin(float height)
    {
        var cd = _controller.CharacterData;
        if (cd == null) return false;
        Vector3 origin = _controller.transform.position + Vector3.up * 0.1f;
        return Physics.Raycast(origin, Vector3.down, height + 0.1f, cd.groundLayer);
    }
}
