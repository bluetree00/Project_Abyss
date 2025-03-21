using UnityEngine;

namespace Game.CharacterStates.States
{

    // 기본 상속할 상태 클래스들

    public class IdleState<T> : State<T> where T : CharacterController
    {
        public override void Enter(T owner)
        {
            Debug.Log($"{typeof(T).Name} - Idle 상태 진입");

        }

        public override void Execute(T owner)
        {
            // 기본 Idle 상태 로직
             owner.Anim?.CrossFade("Idle", 0.2f);
        }

        public override void Exit(T owner)
        {
            Debug.Log($"{typeof(T).Name} - Idle 상태 종료");
        }
    }


    public class MoveState<T> : State<T> where T : CharacterController
    {
        public override void Enter(T owner)
        {
           
        }

        public override void Execute(T owner)
        {
            // owner.Move(owner.moveDirection, owner.CharacterData.baseMoveSpeed);
            owner.Anim?.CrossFade("Moving", 0.1f);
        }

        public override void Exit(T owner) { }
    }

    public class DodgeState<T> : State<T> where T : CharacterController
    {
        public override void Enter(T owner)
        {
             owner.Anim?.CrossFade("Dodge", 0.1f);
        }

        public override void Execute(T owner)
        {
        // owner.Move(owner.moveDirection, owner.CharacterData.baseMoveSpeed);
        
        }

        public override void Exit(T owner) { }
    }

    public class DieState<T> : State<T> where T : CharacterController
    {
        public override void Enter(T owner)
        {
            owner.Anim?.CrossFade("Die", 0.1f);
        }

        public override void Execute(T owner)
        {
          // owner.Move(owner.moveDirection, owner.CharacterData.baseMoveSpeed);
        }

        public override void Exit(T owner) { }
    }

}
