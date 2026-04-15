using System;
using System.Collections.Generic;

public enum GameFlowState
{
    None,
    Logo,
    Login,
    Lobby,
    StageMap,
    InGame,
    Result,
}

public sealed class GameFlow
{
    public GameFlowState CurrentState { get; private set; } = GameFlowState.None;

    public event Action<GameFlowState> OnStateChanged;

    private SceneTransitionManager _sceneTransition;

    // 씬이 상태의 근거: 씬 → 상태 매핑은 GameFlow 내부에서 관리
    private static readonly Dictionary<Define.Scene, GameFlowState> _sceneStateMap = new()
    {
        { Define.Scene.Logo,      GameFlowState.Logo      },
        { Define.Scene.Login,     GameFlowState.Login     },
        { Define.Scene.Lobby,     GameFlowState.Lobby     },
        { Define.Scene.StageMap,  GameFlowState.StageMap  },
        { Define.Scene.GameScene, GameFlowState.InGame    },
        { Define.Scene.Result,    GameFlowState.Result    },
    };

    public void BindSceneTransition(SceneTransitionManager sceneTransition)
    {
        _sceneTransition = sceneTransition;
    }

    public static bool TryGetStateForScene(Define.Scene scene, out GameFlowState state)
        => _sceneStateMap.TryGetValue(scene, out state);

    /// <summary>
    /// 씬만 지정하면 대응하는 GameFlowState로 자동 전환.
    /// 매핑이 없는 씬(Unknown 등)은 경고 후 무시.
    /// </summary>
    public void RequestLoad(Define.Scene scene)
    {
        if (_sceneTransition == null)
        {
            UnityEngine.Debug.LogError("[GameFlow] SceneTransitionManager not bound.");
            return;
        }

        if (!_sceneStateMap.TryGetValue(scene, out var nextState))
        {
            UnityEngine.Debug.LogWarning($"[GameFlow] '{scene}' has no GameFlowState mapping. Request ignored.");
            return;
        }

        if (CurrentState == nextState)
            return;

        _sceneTransition.LoadScene(scene, () => ChangeState(nextState));
    }

    private void ChangeState(GameFlowState newState)
    {
        CurrentState = newState;
        OnStateChanged?.Invoke(newState);
    }
}
