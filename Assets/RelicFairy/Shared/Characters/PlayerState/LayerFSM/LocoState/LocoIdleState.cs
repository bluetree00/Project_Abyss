using UnityEngine;

public class LocoIdleState : ILayerState<LocoState>
{
    private PlayerController _controller;
    private ILayerStateChanger<LocoState> _stateChanger;

    public void Init(PlayerController c, ILayerStateChanger<LocoState> changer)
    {
        _controller = c;
        _stateChanger = changer;
    }

    public void Enter()
    {
        // 공격/스킬 중이면 CrossFade 생략 (공격 애니메이션 덮어쓰기 방지)
        if (!_controller.Combo.IsAttacking)
            _controller.Anim.CrossFade("MoveBlend", 0.1f);
        SetSpeedParam(_controller.Anim, 0f);

        // 정지 → 달리기 상태/대시-후-달리기 요청 해제 (다음 이동은 걷기부터)
        _controller.IsRunning = false;
        _controller.ClearRunAfterDash();
    }

    public void Update()
    {
        var dir = _controller.MoveDirection * _controller.MoveScale;

        // 실제 이동 처리
        _controller.MoveAbility?.Move(_controller, dir);

        // 블렌드 파라미터
        float target = (_controller.Combo.IsAttacking || !_controller.IsGrounded() || _controller.MoveScale < 0.01f) ? 0f : dir.magnitude;
        SetSpeedParam(_controller.Anim, target, 0.12f);

        // Air 전이
        if (!_controller.IsGrounded())
        {
            _stateChanger.Change(LocoState.Air);
            return;
        }

        // Move 전이
        if (target > 0.05f)
        {
            _stateChanger.Change(LocoState.Move);
        }
    }

    public void Exit() { }

    static void SetSpeedParam(Animator anim, float target01, float damp = 0.1f)
        => anim.SetFloat("MoveSpeed", Mathf.Clamp01(target01), damp, Time.deltaTime);
}
