using Game.CharacterStates.StateMachine;

namespace Game.CharacterStates
{
    public static class StateMachineExtensions
    {
        /// <summary>
        /// 상태 머신 초기화와 반환을 동시에 처리하는 유틸 메서드
        /// </summary>
        public static StateMachine<T> SetupAndReturn<T>(this StateMachine<T> sm, T owner, State<T> initialState) where T : UnityEngine.MonoBehaviour
        {
            sm.Setup(owner, initialState);
            return sm;
        }
    }
}
