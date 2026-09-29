using UnityEngine;

public class LocoAirState : LayerStateBase<LocoState>
{
    // MinorDrop: 낙하 높이가 임계 미만 — 추락/착지 애니 생략, 로코모션 유지.
    // [점프 폐기] 상승 단계(Start)는 제거됐다 — 공중 진입이 낙하·넉백뿐이라 도달할 수 없었다.
    private enum AirPhase { MinorDrop, Loop, Landing }

    private const float LandingDuration = 0.15f;

    // CharacterData.minFallAnimHeight 미설정 시 사용할 기본 임계 높이(m).
    private const float DefaultMinFallAnimHeight = 0.6f;

    // 착지 충격 카메라 셰이크 — 낙하 속도(m/s) 이 범위로 강도 매핑. Min 미만은 셰이크 없음(가벼운 단차 착지).
    private const float LandShakeMinSpeed = 6f;
    private const float LandShakeMaxSpeed = 18f;

    private AirPhase _phase;
    private float _landingTimer;
    private float _takeoffY;     // 낙하 시작 시점의 Y (누적 낙하 높이 안전망용)
    private float _fallThreshold; // 이번 낙하의 추락 애니 임계 높이
    private float _maxFallSpeed;  // 체공 중 최대 하강 속도(착지 충격 셰이크 강도용)

    public override void Enter()
    {
        _landingTimer = 0f;
        _maxFallSpeed = 0f;

        // [점프 폐기] 공중 진입은 낙하·넉백뿐이므로 상승 단계가 없다 — 바로 낙하 판정으로 들어간다.
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
            _controller.Anim.CrossFadeInFixedTime("JumpBlend", 0.10f);
        }
    }

    public override void Update()
    {
        switch (_phase)
        {
            case AirPhase.MinorDrop:
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
                    _controller.Anim.CrossFadeInFixedTime("JumpBlend", 0.10f);
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
                _landingTimer -= Time.deltaTime;
                if (_landingTimer <= 0f)
                {
                    float speed = _controller.MoveDirection.magnitude * _controller.MoveScale;
                    _stateChanger.Change(speed > 0.05f ? LocoState.Move : LocoState.Idle);
                }
                break;
        }
    }

    public override void Exit()
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
        _controller.Anim.CrossFadeInFixedTime("JumpLand", 0.08f);

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
