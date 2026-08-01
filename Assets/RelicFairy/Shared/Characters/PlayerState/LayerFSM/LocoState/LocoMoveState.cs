using UnityEngine;

public class LocoMoveState : ILayerState<LocoState>
{
    private PlayerController _controller;
    private ILayerStateChanger<LocoState> _stateChanger;

    // MoveSpeed 댐핑 — 비대칭.
    // 가속측은 아주 살짝만(0.06): 프레임 단위 계단을 뭉개는 정도. 이 값이 곧 출발 지연이라
    // 즉발감을 원하면 여기부터 깎아야 한다(1.5×damp 만큼 도달이 늦어진다).
    // Idle→Walk→Run 3단의 페이스는 이 damp 가 아니라 runRampDuration(목표속도 램프)이 쥔다.
    // 감속측은 작게(0.08): 정지가 늦으면 발이 끌리므로 즉시 따라붙는다.
    private const float BlendDampAccel = 0.06f;
    private const float BlendDampDecel = 0.08f;
    // CharacterData.runRampDuration 미설정 시 사용할 기본 램프 시간(초).
    private const float DefaultRunRamp = 1.0f;

    // 이동 입력 유예(coyote) 창(초). 키보드 방향 반전은 입력이 순간 (0,0)을 지난다
    // (W+S 동시 = Vector2 컴포짓 상쇄, 떼고 누르기 = 30~80ms 공백).
    // 유예가 없으면 그 한 프레임에 Idle로 전이하며 _runCharge01/_animFloor가 리셋돼
    // 달리기를 처음부터 다시 램프한다 — 사용자가 본 "달리다 걷기로 튐"의 마지막 경로.
    // 0.12s: 반전 공백(≤0.08s)은 덮고 진짜 정지의 인지 지연은 감속 램프 길이(≈0.13s) 안에 든다.
    private const float MoveInputGrace = 0.12f;

    private float _runCharge01; // 걷기→달리기 램프 진행도(0→1), 이동 지속 시 차오름
    private bool _forceRun;     // 대시(우클릭) 직후 — 즉시 풀 달리기 유지
    private float _animFloor;   // 이동 입력 유지 중 블렌드 하한(도달한 최고 속도비, 의도 속도로 상한)
    private float _noInputSince; // 이동 입력이 0이 된 시각(음수 = 입력 유지 중). 유예 판정용

    public void Init(PlayerController c, ILayerStateChanger<LocoState> changer)
    {
        _controller = c;
        _stateChanger = changer;
    }

    public void Enter()
    {
        // 공격/스킬 중이면 CrossFade 생략 (공격 애니메이션 덮어쓰기 방지)
        // 회피 종료처럼 자세 차이가 큰 복귀는 RequestLocoBlend로 더 긴 블렌드를 예약해 스냅을 없앤다.
        if (!_controller.Combo.IsAttacking)
            _controller.Anim.CrossFadeInFixedTime("MoveBlend", _controller.ConsumeLocoBlend(0.14f));

        _runCharge01 = 0f;
        _animFloor = 0f;
        _noInputSince = -1f;
        // 대시 직후 진입이면 바로 풀 달리기로 시작.
        _forceRun = _controller.ConsumeRunAfterDash();
        if (_forceRun)
        {
            _runCharge01 = 1f;
            // 애니 블렌드 하한도 풀 달리기로 프라임 — 회피 직후 걷기부터 다시 차오르는 것을 막는다.
            // 첫 Update의 Min(..., IntendedSpeed01)이 실제 상한으로 눌러주므로 과속 포즈는 안 나온다.
            _animFloor = 1f;
        }
    }

    public void Update()
    {
        var dir = _controller.MoveDirection * _controller.MoveScale;
        bool hasInput = dir.sqrMagnitude > 0.0001f;

        // 입력 유예(coyote) — "진짜로 멈춤"과 "방향 반전 순간 공백"을 시간으로 구분한다.
        // 입력이 0이 된 시각을 찍어두고, MoveInputGrace 안이면 여전히 '이동 중'으로 취급한다.
        // 유예 중 입력이 돌아오면 _noInputSince가 다시 음수가 되어 상태 전이·리셋이 아예 일어나지 않는다.
        if (hasInput) _noInputSince = -1f;
        else if (_noInputSince < 0f) _noInputSince = Time.time;
        bool inGrace = !hasInput && Time.time - _noInputSince < MoveInputGrace;
        bool moving = hasInput || inGrace;

        // 이동 지속 시 걷기→달리기 램프(무장비 포함). 대시 직후(_forceRun)면 즉시 풀 달리기, 아니면 램프 시간 동안 점진 가속.
        bool canRun = moving;
        if (canRun)
        {
            if (_forceRun)
            {
                _runCharge01 = 1f;
            }
            else if (hasInput) // 유예 중에는 램프를 '유지'만 하고 더 차오르지 않게 한다(입력이 없는 구간이므로).
            {
                var cd = _controller.CharacterData;
                float ramp = (cd != null && cd.runRampDuration > 0.01f) ? cd.runRampDuration : DefaultRunRamp;
                _runCharge01 = Mathf.Min(1f, _runCharge01 + Time.deltaTime / ramp);
            }
        }
        else
        {
            _runCharge01 = 0f; // 유예까지 지난 진짜 정지 — 리셋
        }

        // 가벼운 ease-in-out 보간율 → 속도/애니 공통 적용
        float runBlend = canRun ? Mathf.SmoothStep(0f, 1f, _runCharge01) : 0f;
        _controller.RunBlend01 = runBlend;
        bool running = canRun && _runCharge01 >= 1f;
        _controller.IsRunning = running;

        // 실제 이동 처리 (Move가 RunBlend01로 속도 보간)
        _controller.MoveAbility?.Move(_controller, dir);

        // 블렌드 파라미터 — 실속도 기반이되, 이동 입력이 유지되는 동안은 '이미 도달한 속도비'를 하한으로 깐다.
        // 실속도만 쓰면 달리는 중에 하위 상태로 튄다: MoveTowards가 속도벡터 공간을 직선으로 가로지르므로
        // 90° 선회만으로 크기가 1/√2(0.707)로, 180° 반전이면 0까지 떨어지고, groundDrag(4)로 인한
        // 프레임 리플까지 겹쳐 Walk/Idle 구간을 찍는다. 하한은 의도 속도(IntendedSpeed01)를 넘지 않으므로
        // 출발 시 Idle→Walk→Run 램프는 그대로 살아있고, 이동 감속 버프에서도 발이 미끄러지지 않는다.
        // 올라갈 땐 실속도를 따라가고 내려올 땐 붙잡는 비대칭 = run↔walk 경계 히스테리시스.
        // 유예 중에는 하한을 갱신하지 않고 '홀드'한다 — dir이 0이라 Move가 IntendedSpeed01을 0으로
        // 내려놓으므로, 평소 규칙(의도 속도로 상한)을 그대로 적용하면 하한이 0으로 무너져 유예가 무의미해진다.
        float actual01 = _controller.HorizontalSpeed01;
        if (hasInput)
            _animFloor = Mathf.Min(Mathf.Max(_animFloor, actual01), _controller.IntendedSpeed01);
        else if (!inGrace)
            _animFloor = 0f;
        float animSpeed = moving ? Mathf.Max(actual01, _animFloor) : 0f;
        SetSpeedParam(_controller.Anim, animSpeed);

        // Air 전이 — 스텝 오르는 중엔 잠깐 공중 판정이 떠도 낙하 상태로 빠지지 않음.
        if (!_controller.IsGrounded() && !_controller.IsStepClimbing)
        {
            _stateChanger.Change(LocoState.Air);
            return;
        }

        // Idle 전이 — 입력이 끊긴 뒤 유예(MoveInputGrace)까지 지나야 넘어간다.
        // Air/회피/공격 전이는 위/외부에서 처리되므로 유예가 막지 않는다.
        if (!moving)
            _stateChanger.Change(LocoState.Idle);
    }

    public void Exit() => _controller.IsRunning = false;

    // 목표가 현재보다 크면 가속(느린 damp), 작으면 감속(빠른 damp).
    static void SetSpeedParam(Animator anim, float target01)
    {
        float t = Mathf.Clamp01(target01);
        float damp = t > anim.GetFloat("MoveSpeed") ? BlendDampAccel : BlendDampDecel;
        anim.SetFloat("MoveSpeed", t, damp, Time.deltaTime);
    }
}
