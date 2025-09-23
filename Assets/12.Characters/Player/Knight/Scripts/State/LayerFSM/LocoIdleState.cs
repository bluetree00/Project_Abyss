using UnityEngine;

public class LocoIdleState : ILayerState<LocoState>
{
    private PlayerController _controller;
    private ILayerStateChanger<LocoState> _stateChanger;

    public void Init(PlayerController c, ILayerStateChanger<LocoState> changer)
    { _controller = c; _stateChanger = changer; }

    public void Enter()
    {
        _controller.Anim.CrossFade("MoveBlend", 0.1f);
        SetSpeedParam(_controller.Anim, 0f); // 진입 시 0으로 수렴
    }

    public void Update()
    {
        var dir = _controller.IsMoveLocked ? Vector3.zero
                                           : _controller.MoveDirection * _controller.MoveScale;

        // 실제 이동 처리
        _controller.MoveAbility?.Move(_controller, dir);

        // 블렌드 파라미터(0~1)
        float target = dir.magnitude;                    // MoveDirection이 정규화라면 0~1
        SetSpeedParam(_controller.Anim, target, 0.12f);  // 약간 느긋한 감속

        // 전이
        if (!_controller.IsGrounded())
            _stateChanger.Change(LocoState.Air);
        else if (target > 0.05f) // 데드존
            _stateChanger.Change(LocoState.Move);
    }
    public void Exit() { }

    // 유틸 (파일 상단으로 올려도 됨)
    static void SetSpeedParam(Animator anim, float target01, float damp = 0.1f)
        => anim.SetFloat("MoveSpeed", Mathf.Clamp01(target01), damp, Time.deltaTime);
}
