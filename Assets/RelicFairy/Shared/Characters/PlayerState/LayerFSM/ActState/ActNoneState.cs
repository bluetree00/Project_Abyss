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

        // 지상일 때만 MoveBlend로 부드럽게 복귀
        if (_controller.IsGrounded())
        {
            var anim = _controller.Anim;
            // normalizedTimeOffset 인자를 주지 않는다 — 0으로 주면 공격이 끝날 때마다
            // 보행 사이클이 0프레임으로 리셋돼 발 위치가 툭 튄다. 현재 위상을 유지한 채 이어붙인다.
            if (anim != null)
                anim.CrossFadeInFixedTime("MoveBlend", 0.14f);
        }
    }

    public void Update() { }
    public void Exit() { }
}
