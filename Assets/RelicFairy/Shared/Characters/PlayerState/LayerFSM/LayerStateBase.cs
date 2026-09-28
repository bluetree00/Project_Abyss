using System;

/// <summary>
/// 레이어 상태 공통 기반 — 컨트롤러·상태 전환기 보관과 기본(빈) Update/Exit.
///
/// 모든 이동(Loco)·행동(Act) 상태가 같은 Init 코드를 복붙하던 것을 한곳으로 모았다.
/// 파생 상태는 Enter만 반드시 구현하고, 필요한 경우에만 Update/Exit를 재정의한다.
/// </summary>
public abstract class LayerStateBase<TId> : ILayerState<TId> where TId : struct, Enum
{
    protected PlayerController        _controller;
    protected ILayerStateChanger<TId> _stateChanger;

    public void Init(PlayerController controller, ILayerStateChanger<TId> stateChanger)
    {
        _controller   = controller;
        _stateChanger = stateChanger;
    }

    public abstract void Enter();
    public virtual void Update() { }
    public virtual void Exit() { }
}
