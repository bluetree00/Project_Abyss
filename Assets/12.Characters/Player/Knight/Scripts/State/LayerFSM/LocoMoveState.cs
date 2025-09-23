using UnityEngine;

public class LocoMoveState : ILayerState<LocoState>
{
    private PlayerController _controller;
    private ILayerStateChanger<LocoState> _stateChanger;

    // 런 블렌드를 상태에 보관 (0 = 완전 걷기, 1 = 완전 달리기)
    // 상태마다 유지하면 입력 토글시에도 부드럽게 바뀜
    private float _runBlend = 0f;

    // 런 블렌드가 얼마나 빨리 목표값에 도달할지 (1~30 정도 추천)
    private float _runBlendSpeed = 10f;

    public void Init(PlayerController c, ILayerStateChanger<LocoState> changer)
    {
        _controller = c;
        _stateChanger = changer;
    }

    public void Enter()
    {
        // 같은 블렌드 트리 유지
        _controller.Anim.CrossFade("MoveBlend", 0.05f);
        // Enter 시 현재 isRunChecked 상태를 기반으로 초기화 (선택)
        _runBlend = _controller.IsRunChecked ? 1f : 0f;
    }

    public void Update()
    {
        var dir = _controller.IsMoveLocked ? Vector3.zero
                                           : _controller.MoveDirection * _controller.MoveScale;

        _controller.MoveAbility?.Move(_controller, dir);

        // 입력 크기 (0 ~ 1)
        float moveInput = dir.magnitude;

        // _controller.isRunChecked 는 input callback에서 세팅되는 bool 이라고 가정
        float targetRun = _controller.IsRunChecked ? 1f : 0f;

        // 부드럽게 runBlend를 목표값으로 이동시킴
        // Time.deltaTime 곱을 통해 프레임 독립적 제어
        _runBlend = Mathf.MoveTowards(_runBlend, targetRun, _runBlendSpeed * Time.deltaTime);

        // 걷기/달리기 값 산정 방법:
        // - 걷기 최대은 0.5, 달리기 최대는 1.0
        // - moveInput(0..1)을 곱해 이동 강도에 따라 값이 커짐
        // - _runBlend(0..1)로 걷기와 달리기 사이를 보간
        float walkValue = 0.5f * moveInput; // 0 .. 0.5
        float runValue  = 1.0f * moveInput; // 0 .. 1.0
        float target = Mathf.Lerp(walkValue, runValue, _runBlend);

        // 애니메이션 파라미터 세팅 (damp은 빠르게 반응하도록 약간 작게 설정)
        SetSpeedParam(_controller.Anim, target, 0.08f);

        // 상태 전이
        if (!_controller.IsGrounded())
            _stateChanger.Change(LocoState.Air);
        else if (moveInput <= 0.05f)
            _stateChanger.Change(LocoState.Idle);
    }

    public void Exit() { }

    static void SetSpeedParam(Animator anim, float target01, float damp = 0.1f)
        => anim.SetFloat("MoveSpeed", Mathf.Clamp01(target01), damp, Time.deltaTime);
}
