using UnityEngine;

namespace Game.CharacterStates.CharacterControllerStates
{

    public abstract class AnimationState<T> : State<T> where T : CharacterController
    {
        private string _animName;
        private float _endTime = 0.95f;
        private int _layer = 0;
        private bool _blocksInput = true;

        public override bool BlocksInput => _blocksInput;

        protected void SetupAnimation(string animName, float endTime = 0.95f, int layer = 0)
        {
            _animName = animName;
            _endTime = endTime;
            _layer = layer;
        }

        public override void Enter(T owner)
        {
            owner.Anim.CrossFade(_animName, 0.1f);
        }

        public override void Execute(T owner)
        {
            if (owner.Anim.IsInTransition(_layer)) return; // 전이 중이면 건너뜀
            
            if (AnimationHelper.IsAnimationFinished(owner.Anim, _animName, _endTime, _layer))
            {
                _blocksInput = false;
                OnAnimationEnd(owner);
            }
        }

        public override void Exit(T owner)
        {
            _blocksInput = false;
        }

        protected abstract void OnAnimationEnd(T owner);
    }


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