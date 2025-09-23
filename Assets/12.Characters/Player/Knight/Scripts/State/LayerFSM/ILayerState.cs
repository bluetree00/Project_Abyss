// /Scripts/FSM/ILayerState.cs
using System;

public interface ILayerState<TId> where TId : struct, Enum
{
    void Init(PlayerController controller, ILayerStateChanger<TId> stateChanger);
    void Enter();
    void Update();
    void Exit();
}

public interface ILayerStateChanger<TId> where TId : struct, Enum
{
    void Change(TId next);
}
