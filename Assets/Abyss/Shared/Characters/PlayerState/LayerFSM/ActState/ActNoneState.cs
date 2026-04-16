public class ActNoneState : ILayerState<ActState> //act 상태의 idle의 역활을 수행해야함 
{
    private PlayerController _controller;
    private ILayerStateChanger<ActState> _stateChanger;

    public void Init(PlayerController controller, ILayerStateChanger<ActState> stateChanger)
    {
        _controller = controller;
        _stateChanger = stateChanger;
    }

    public void Enter()
    {
        // 공격 플래그만 초기화 — LocoSM은 건드리지 않는다.
        // (공중에서 공격이 끝나도 LocoState.Air는 유지되어야 착지 감지가 계속 동작)
        _controller.Combo.SetAttacking(false);

        // 지상일 때만 MoveBlend로 부드럽게 복귀 (착지 애니메이션 중에는 스킵)
        if (_controller.IsGrounded() && !_controller.IsLanding)
        {
            var anim = _controller.Anim;
            if (anim != null && !anim.IsInTransition(0))
                anim.CrossFade("MoveBlend", 0.08f, 0, 0f);
        }
    }

    public void Update() { }
    public void Exit() { }
}
