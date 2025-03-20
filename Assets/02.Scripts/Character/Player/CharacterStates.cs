using UnityEngine;

namespace Game.CharacterStates
{
    // 제네릭 상태 추상 클래스: 소유자의 타입 T를 받아들임
    public abstract class State<T> where T : MonoBehaviour
    {
        /// <summary>
        /// 상태 진입 시 호출됩니다.
        /// </summary>
        public abstract void Enter(T owner);

        /// <summary>
        /// 상태 실행 중 매 프레임 호출됩니다.
        /// </summary>
        public abstract void Execute(T owner);

        /// <summary>
        /// 상태 종료 시 호출됩니다.
        /// </summary>
        public abstract void Exit(T owner);
    }

    // 제네릭 상태 머신 클래스: 소유자 타입 T에 대해 작동 (예: CharacterController)
    public class StateMachine<T> where T : MonoBehaviour
    {
        private T ownerEntity;            // 상태 머신의 소유 객체
        private State<T> currentState;    // 현재 상태
        private State<T> previousState;   // 이전 상태

        /// <summary>
        /// 상태 머신을 설정합니다.
        /// </summary>
        /// <param name="owner">상태 머신이 속한 객체</param>
        /// <param name="initialState">초기 상태</param>
        public void Setup(T owner, State<T> initialState)
        {
            ownerEntity = owner;
            currentState = null;
            ChangeState(initialState);
        }

        /// <summary>
        /// 매 프레임 현재 상태의 Execute를 호출합니다.
        /// </summary>
        public void Update()
        {
            currentState?.Execute(ownerEntity);
        }

        /// <summary>
        /// 상태 전환을 수행합니다.
        /// </summary>
        /// <param name="newState">새로운 상태</param>
        public void ChangeState(State<T> newState)
        {
            if (newState == null)
            {
                Debug.LogError("새로운 상태가 null입니다.");
                return;
            }

            // 현재 상태 종료
            currentState?.Exit(ownerEntity);
            // 이전 상태 저장
            previousState = currentState;
            // 상태 변경 및 진입
            currentState = newState;
            currentState.Enter(ownerEntity);
        }

        /// <summary>
        /// 이전 상태로 복귀합니다.
        /// </summary>
        public void RevertState()
        {
            if (previousState != null)
            {
                ChangeState(previousState);
            }
        }
    }
}
