using UnityEngine;

namespace Game.CharacterStates.States
{

    // 기본 상속할 상태 클래스들

   public class IdleState<T> : State<T> where T : CharacterController
    {
        public override void Enter(T owner)
        {
            owner.Anim.CrossFade("Idle", 0.2f);

            if (owner.weaponContainer != null && 
                    owner.weaponContainer.isWeaponEquipped && 
                    owner.currentWeapon.weapon_Idle_AnimationName != "")
                    {   
                        owner.Anim.CrossFade($"{owner.currentWeapon.weapon_Idle_AnimationName}", 0.1f);
                    }
                    else
                    {
                        owner.Anim.CrossFade("Idle", 0.2f);
                    } 
        }

        public override void Execute(T owner)
        {
            // Idle 상태에서 지속 로직이 필요한 경우 여기에
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
             owner.Anim?.CrossFade("Moving", 0.1f);
        }

        public override void Execute(T owner)
        {
            // owner.Move(owner.moveDirection, owner.CharacterData.baseMoveSpeed);
        }

        public override void Exit(T owner) { }
    }

    public class RuningState<T> : State<T> where T : CharacterController
    {
        public override void Enter(T owner)
        {
           
        }

        public override void Execute(T owner)
        {
            owner.Anim?.CrossFade("Runing", 0.1f);
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
        }

        public override void Exit(T owner) { }
    }

}
