// Scripts/Game/CharacterStates/StateMachine/AttackStateMachine.cs

using Game.CharacterStates;
using System;

namespace Game.CharacterStates.StateMachine
{
    /// <summary>
    /// 공격용 서브 상태 머신 - 콤보 입력 큐 및 상태 흐름 관리
    /// </summary>
    public class AttackStateMachine<T> : StateMachine<T> where T : CharacterController
    {
        public bool NextComboQueued { get; private set; }

        public void QueueNextCombo()
        {
            NextComboQueued = true;
        }

        public void ResetQueue()
        {
            NextComboQueued = false;
        }
    }
}
