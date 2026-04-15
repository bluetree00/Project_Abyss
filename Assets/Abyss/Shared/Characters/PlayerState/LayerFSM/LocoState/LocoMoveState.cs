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
        // 공격/스킬 중이면 CrossFade 생략 (공격 애니메이션 덮어쓰기 방지)
        if (!_controller.Combo.IsAttacking)
            _controller.Anim.CrossFade("MoveBlend", 0.05f);
        _runBlend = _controller.IsRunChecked ? 1f : 0f;
    }

    public void Update()
{
    // 이전: var dir = _controller.IsMoveLocked ? Vector3.zero : _controller.MoveDirection * _controller.MoveScale;
    var dir = _controller.MoveDirection * _controller.MoveScale; // 항상 이동

    // 실제 이동 처리
    _controller.MoveAbility?.Move(_controller, dir);

    // 블렌드 파라미터
    float target = dir.magnitude;
    SetSpeedParam(_controller.Anim, target, 0.12f);

    // 전이
    if (!_controller.IsGrounded())
        _stateChanger.Change(LocoState.Air);
    else if (target < 0.01f)
        _stateChanger.Change(LocoState.Idle);
}


    public void Exit() { }

    static void SetSpeedParam(Animator anim, float target01, float damp = 0.1f)
        => anim.SetFloat("MoveSpeed", Mathf.Clamp01(target01), damp, Time.deltaTime);
}
