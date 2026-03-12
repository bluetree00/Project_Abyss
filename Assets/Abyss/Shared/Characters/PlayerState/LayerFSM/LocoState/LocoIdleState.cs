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
        _controller.Anim.CrossFade("MoveBlend", 0.1f);
        SetSpeedParam(_controller.Anim, 0f); // 진입 시 0으로 수렴
    }

    public void Update()
    {
        // MoveLock 제거: 항상 MoveDirection × MoveScale 적용
        var dir = _controller.MoveDirection * _controller.MoveScale;

        // 실제 이동 처리
        _controller.MoveAbility?.Move(_controller, dir);

        // 블렌드 파라미터(0~1)
        float target = (_controller.Combo.IsAttacking || !_controller.IsGrounded()) ? 0f : dir.magnitude;
        SetSpeedParam(_controller.Anim, target, 0.12f);


          // Air 전이
        if (!_controller.IsGrounded() && !_controller.isJumping)
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

    // 유틸
    static void SetSpeedParam(Animator anim, float target01, float damp = 0.1f)
        => anim.SetFloat("MoveSpeed", Mathf.Clamp01(target01), damp, Time.deltaTime);
}
