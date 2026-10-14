public class ActNoneState : LayerStateBase<ActState> //act 상태의 idle의 역활을 수행해야함 
{
    public override void Enter()
    {
        // 공격 플래그만 초기화 — LocoSM은 건드리지 않는다.
        // (공중에서 공격이 끝나도 LocoState.Air는 유지되어야 착지 감지가 계속 동작)
        _controller.Combo.SetAttacking(false);

        // 지상일 때만 MoveBlend로 부드럽게 복귀
        if (_controller.IsGrounded())
        {
            // normalizedTimeOffset 인자를 주지 않는다 — 0으로 주면 공격이 끝날 때마다
            // 보행 사이클이 0프레임으로 리셋돼 발 위치가 툭 튄다. 현재 위상을 유지한 채 이어붙인다.
            // 이미 이동 블렌드에 있으면 건너뛴다(10-09 출시 점검 — 이동 중 자기 강화 뒤 보행이 처음으로 돌아갔다)
            _controller.CrossFadeMoveBlend(0.14f);
        }
    }
}
