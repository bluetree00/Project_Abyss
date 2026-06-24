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

        // 실제 이동 처리 (입력 0이면 Move가 감속 램프로 정지 — 잔여속도가 한동안 남음)
        _controller.MoveAbility?.Move(_controller, dir);

        bool moveBlocked = _controller.Combo.IsAttacking || !_controller.IsGrounded() || _controller.MoveScale < 0.01f;
        // 애니: 실제 수평 속도비율로 구동(감속 잔여속도 반영 → 발미끄러짐 방지). 차단 시 0.
        float animSpeed = moveBlocked ? 0f : _controller.HorizontalSpeed01;
        SetSpeedParam(_controller.Anim, animSpeed, 0.08f);

        // Air 전이 — 스텝 오르는 중엔 잠깐 공중 판정이 떠도 낙하 상태로 빠지지 않음.
        if (!_controller.IsGrounded() && !_controller.IsStepClimbing)
        {
            _stateChanger.Change(LocoState.Air);
            return;
        }

        // Move 전이는 입력 기준(감속 잔여속도로 Move 복귀 방지)
        if (!moveBlocked && dir.magnitude > 0.05f)
        {
            _stateChanger.Change(LocoState.Move);
        }
    }

    public void Exit() { }

    static void SetSpeedParam(Animator anim, float target01, float damp = 0.1f)
        => anim.SetFloat("MoveSpeed", Mathf.Clamp01(target01), damp, Time.deltaTime);
}
