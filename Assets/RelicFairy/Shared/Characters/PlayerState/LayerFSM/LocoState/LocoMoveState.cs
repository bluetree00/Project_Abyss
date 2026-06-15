using UnityEngine;

public class LocoMoveState : ILayerState<LocoState>
{
    private PlayerController _controller;
    private ILayerStateChanger<LocoState> _stateChanger;

    // MoveSpeed 댐핑(작게 — 속도 평활은 가속 모델 한 곳에서만, 여긴 미세 떨림만 제거).
    private const float BlendDamp = 0.08f;
    // CharacterData.runRampDuration 미설정 시 사용할 기본 램프 시간(초).
    private const float DefaultRunRamp = 1.0f;

    private float _runCharge01; // 걷기→달리기 램프 진행도(0→1), 이동 지속 시 차오름
    private bool _forceRun;     // 대시(우클릭) 직후 — 즉시 풀 달리기 유지

    public void Init(PlayerController c, ILayerStateChanger<LocoState> changer)
    {
        _controller = c;
        _stateChanger = changer;
    }

    public void Enter()
    {
        // 공격/스킬 중이면 CrossFade 생략 (공격 애니메이션 덮어쓰기 방지)
        if (!_controller.Combo.IsAttacking)
            _controller.Anim.CrossFade("MoveBlend", 0.05f);

        _runCharge01 = 0f;
        // 대시 직후 진입이면 바로 풀 달리기로 시작(장비 보유 시에만).
        _forceRun = _controller.HasWeapon && _controller.ConsumeRunAfterDash();
        if (_forceRun) _runCharge01 = 1f;
    }

    public void Update()
    {
        var dir = _controller.MoveDirection * _controller.MoveScale;
        bool moving = dir.sqrMagnitude > 0.0001f;

        // 무장비=걷기만. 장비(무기) 보유 시: 대시 직후(_forceRun)는 즉시 풀, 아니면 램프 시간 동안 점진 가속.
        bool canRun = moving && _controller.HasWeapon;
        if (canRun)
        {
            if (_forceRun)
            {
                _runCharge01 = 1f;
            }
            else
            {
                var cd = _controller.CharacterData;
                float ramp = (cd != null && cd.runRampDuration > 0.01f) ? cd.runRampDuration : DefaultRunRamp;
                _runCharge01 = Mathf.Min(1f, _runCharge01 + Time.deltaTime / ramp);
            }
        }
        else
        {
            _runCharge01 = 0f; // 정지 시 즉시 리셋
        }

        // 가벼운 ease-in-out 보간율 → 속도/애니 공통 적용
        float runBlend = canRun ? Mathf.SmoothStep(0f, 1f, _runCharge01) : 0f;
        _controller.RunBlend01 = runBlend;
        bool running = canRun && _runCharge01 >= 1f;
        _controller.IsRunning = running;

        // 실제 이동 처리 (Move가 RunBlend01로 속도 보간)
        _controller.MoveAbility?.Move(_controller, dir);

        // 블렌드 파라미터 — 실제 수평 속도비율로 구동(가속 램프·runCharge·정지감속이 애니에 자동 반영, 발미끄러짐 해소).
        float animSpeed = moving ? _controller.HorizontalSpeed01 : 0f;
        SetSpeedParam(_controller.Anim, animSpeed, BlendDamp);

        // Air 전이
        if (!_controller.IsGrounded())
        {
            _stateChanger.Change(LocoState.Air);
            return;
        }

        // Idle 전이 (실제 이동 입력 기준)
        if (!moving)
            _stateChanger.Change(LocoState.Idle);
    }

    public void Exit() => _controller.IsRunning = false;

    static void SetSpeedParam(Animator anim, float target01, float damp)
        => anim.SetFloat("MoveSpeed", Mathf.Clamp01(target01), damp, Time.deltaTime);
}
