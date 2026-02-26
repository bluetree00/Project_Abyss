using System;


public enum GameFlowState
{
    None,
    Title,
    Lobby,
    InGame,
    Result,
}

public sealed class GameFlowManager
{
    public GameFlowState CurrentState { get; private set; } = GameFlowState.None;

    public event Action<GameFlowState> OnStateChanged;

    private SceneTransitionManager _sceneTransition;

    public void BindSceneTransition(SceneTransitionManager sceneTransition)
    {
        _sceneTransition = sceneTransition;
    }

    /// <summary>
    /// Flow 상태 전환 요청
    /// </summary>
    public void RequestLoad(Define.Scene scene, GameFlowState nextState)
    {
        if (_sceneTransition == null)
        {
            UnityEngine.Debug.LogError("[GameFlow] SceneTransitionManager not bound.");
            return;
        }

        if (CurrentState == nextState)
            return;

        _sceneTransition.LoadScene(scene, () =>
        {
            ChangeState(nextState);
        });
    }

    private void ChangeState(GameFlowState newState)
    {
        CurrentState = newState;
        OnStateChanged?.Invoke(newState);
    }
}