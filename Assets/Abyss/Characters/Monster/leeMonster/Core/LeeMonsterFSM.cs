using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 제네릭하지 않은 단순 FSM.
/// 상태 딕셔너리를 주입받아 Enter / Update / Exit 를 라우팅한다.
/// </summary>
public class LeeMonsterFSM
{
    private readonly Dictionary<LeeMonsterStateType, ILeeMonsterState> _states;
    private ILeeMonsterState _currentState;
    private readonly LeeMonsterContext _ctx;

    public LeeMonsterStateType CurrentType { get; private set; }

    public LeeMonsterFSM(
        LeeMonsterContext ctx,
        Dictionary<LeeMonsterStateType, ILeeMonsterState> states)
    {
        _ctx    = ctx;
        _states = states;
    }

    /// <summary>
    /// 상태 전환 요청. 같은 상태로의 전환은 무시한다.
    /// </summary>
    public void ChangeState(LeeMonsterStateType type)
    {
        if (CurrentType == type && _currentState != null) return;

        _currentState?.Exit(_ctx);
        CurrentType   = type;

        if (!_states.TryGetValue(type, out _currentState))
        {
            Debug.LogError($"[LeeMonsterFSM] 등록되지 않은 상태: {type}");
            return;
        }

        _currentState.Enter(_ctx);
    }

    /// <summary>매 프레임 현재 상태 Update 실행.</summary>
    public void Update()
    {
        _currentState?.Update(_ctx);
    }
}
