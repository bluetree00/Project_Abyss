using System;
using Game.CharacterStates.MonsterControllerStates;
using UnityEngine;

namespace Game.CharacterStates
{
    // 상태 머신 클래스
    public class StateMachine<T> where T : MonoBehaviour
{
    private T ownerEntity;
    private State<T> currentState;
    private State<T> previousState;

    public State<T> CurrentState => currentState;

    public void Setup(T owner, State<T> initialState)
    {
        ownerEntity = owner;
        ChangeState(initialState);
    }

    public void Update()
    {
        currentState?.Execute(ownerEntity);
    }

    public void ChangeState(State<T> newState)
    {
        if (newState == null) return;

        // 동일 타입의 상태이며 반복이 허용되지 않으면 전환 막기
        if (currentState != null &&
            currentState.GetType() == newState.GetType() &&
            !newState.CanRepeat)
        {
            return;
        }

        currentState?.Exit(ownerEntity);
        previousState = currentState;
        currentState = newState;
        currentState.Enter(ownerEntity);
    }

    public void RevertState()
    {
        if (previousState != null)
        {
            ChangeState(previousState);
        }
    }

        internal void ChangeState<T>(MoveState<T> moveState) where T : MonsterController
        {
            throw new NotImplementedException();
        }
    }

}