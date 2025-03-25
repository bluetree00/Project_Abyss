
using UnityEngine;

namespace Game.CharacterStates.MonsterControllerStates
{
    public class IdleState<T> : State<T> where T : MonsterController
    {
        public override void Enter(T owner)
        {
          
        }

        public override void Execute(T owner)
        {
             GameObject player = GameObject.FindGameObjectWithTag("Player");
            if (player == null)
                return;

            float distance = (player.transform.position - owner.transform.position).magnitude;
            if (distance <= owner.MonsterData._scacRange)
            {
                owner._lockTarget = player;
                owner.StateMachine.ChangeState(new MoveState<T>());
                return;
            }
        }

        public override void Exit(T owner)
        {
            Debug.Log($"{typeof(T).Name} - Idle 상태 종료");
        }
    }

    public class MoveState<T> : State<T> where T : MonsterController
    {
        public override void Enter(T owner)
        {
          
        }

        public override void Execute(T owner)
        {
        
             
        }

        public override void Exit(T owner)
        {
          
        }
    }

}