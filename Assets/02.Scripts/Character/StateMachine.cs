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
}

}
