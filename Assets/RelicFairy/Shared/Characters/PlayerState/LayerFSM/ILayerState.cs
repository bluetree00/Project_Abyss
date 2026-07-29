using System;

/// <summary>
/// 레이어 상태 인터페이스
/// </summary>
public interface ILayerState<TId> where TId : struct, Enum
{
    void Init(PlayerController controller, ILayerStateChanger<TId> stateChanger);
    void Enter();
    void Update();
    void Exit();
}

/// <summary>
/// 상태 전이 인터페이스
/// </summary>
public interface ILayerStateChanger<TId> where TId : struct, Enum
{
    void Change(TId next);
}
