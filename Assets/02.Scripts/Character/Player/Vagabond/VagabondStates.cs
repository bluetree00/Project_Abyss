using UnityEngine;
using Game.CharacterStates.CharacterControllerStates;

namespace Game.CharacterStates.VagabondStates
{

    public class VagabondIdleState : IdleState<Vagabond> { }

      public class VagabondMoveBlendState : State<Vagabond>
    {
        public override void Enter(Vagabond owner)
        {
            owner.Anim.CrossFade("MoveBlend", 0.1f);
        }

        public override void Execute(Vagabond owner)
        {
             if (!owner.CanProcessInput())
             return;

            float moveAmount = owner.MoveDirection.magnitude;
            float targetSpeed = moveAmount > 0
                ? (Input.GetKey(KeyCode.LeftShift) ? 1f : 0.5f)
                : 0f;

            owner.Anim.SetFloat("MoveSpeed", targetSpeed, 0.1f, Time.deltaTime);

            float moveSpeed = targetSpeed >= 0.9f
                ? owner.CharacterData.baseRunSpeed
                : owner.CharacterData.baseMoveSpeed;
        }

        public override void Exit(Vagabond owner)
        {
            owner.Anim.SetFloat("MoveSpeed", 0f);
        }
    }


    public class VagabondDodgeState : AnimationState<Vagabond>
    {
        public override void Enter(Vagabond owner)
        {
            SetupAnimation("Dodge", 0.6f);
            base.Enter(owner);
        }

        protected override void OnAnimationEnd(Vagabond owner) { }
    }

    public class VagabondComboAttackState : AnimationState<Vagabond>
    {
        //public override bool BlocksInput => true;
        private int comboIndex;

        private bool blocksInput = true;
        public override bool BlocksInput => blocksInput;

        public void SetComboIndex(int index)
        {
            comboIndex = index;
        }

        public override void Enter(Vagabond owner)
        {
            blocksInput = true;
            
            if (owner.weaponManagerSO.CurrentWeapon == null)
            {
                Debug.LogError("무기가 없습니다. 콤보 공격 불가.");
                owner.StateMachine.ChangeState(owner.GetState<VagabondIdleState>());
                return;
            }

            var weapon = owner.weaponManagerSO.CurrentWeapon;
            int index = Mathf.Clamp(comboIndex, 0, weapon.normalAttackAnimations.Length - 1);

            string animName = weapon.normalAttackAnimations[index];
            float endTime = weapon.comboEndTimes[index];

            SetupAnimation(animName, endTime);

            owner.OnAttackAnimationStart(); // ✅ 공격 상태 시작 알림
            base.Enter(owner);
        }
        

        public override void Exit(Vagabond owner)
        {
            blocksInput = false;
            owner.OnAttackAnimationEnd();
        }

        protected override void OnAnimationEnd(Vagabond owner)
        {
            owner.OnAttackAnimationEnd(); // ✅ 공격 상태 종료 알림 및 예약 콤보 처리

            // 다음 입력 예약이 없는 경우만 상태 전환 (중복 방지)
            if (!owner.IsAttacking)
            {
                owner.StateMachine.ChangeState(owner.GetState<VagabondIdleState>());
            }
        }
    }




    public class VagabondChangeWeaponState : AnimationState<Vagabond>
    {
        public override void Enter(Vagabond owner)
        {
            string anim = owner.weaponManagerSO.CurrentWeapon?.weapon_ChangeWeapon_AnimationName;
            if (!string.IsNullOrEmpty(anim))
            {
                SetupAnimation(anim, 0.95f);
                base.Enter(owner);
            }
            else
            {
                Debug.LogWarning("무기 변경 애니메이션 이름이 비어있거나 무기가 없습니다.");
            }
        }

        protected override void OnAnimationEnd(Vagabond owner)
        {
            owner.StateMachine.ChangeState(new VagabondIdleState());
        }
    }

    public class VagabondSkillState : AnimationState<Vagabond>
    {
        public override void Enter(Vagabond owner)
        {
            SetupAnimation("NormalSkile_01");
            base.Enter(owner);
        }

        protected override void OnAnimationEnd(Vagabond owner)
        {
            owner.StateMachine.ChangeState(new VagabondIdleState());
        }
    }

    public class VagabondUltimateState : AnimationState<Vagabond>
    {
        public override void Enter(Vagabond owner)
        {
            SetupAnimation("UltimateSkile_01");
            base.Enter(owner);
        }

        protected override void OnAnimationEnd(Vagabond owner)
        {
            owner.StateMachine.ChangeState(new VagabondIdleState());
        }
    }

    public class VagabondJumpStartState : AnimationState<Vagabond>
    {
        public override void Enter(Vagabond owner)
        {
            owner.SetAirState(CharacterController.AirState.JumpStart);
            SetupAnimation("Jump_Start", 0.9f);
            base.Enter(owner);
            owner.Jump();
        }

        protected override void OnAnimationEnd(Vagabond owner)
        {
            owner.StateMachine.ChangeState(new VagabondInAirState());
        }
    }

    public class VagabondInAirState : State<Vagabond>
    {
        public override bool BlocksInput => true;

        public override void Enter(Vagabond owner)
        {
            owner.SetAirState(CharacterController.AirState.InAir);
            owner.Anim.CrossFade("Jump_Loop", 0.1f);
        }

        public override void Execute(Vagabond owner)
        {
            if (owner.IsGrounded())
            {
                owner.StateMachine.ChangeState(
                    owner.IsHardLanding
                    ? new VagabondHardLandingState()
                    : new VagabondLandingState());
            }
        }

        public override void Exit(Vagabond owner) { }
    }

    public class VagabondLandingState : AnimationState<Vagabond>
    {
        public override void Enter(Vagabond owner)
        {
            owner.SetAirState(CharacterController.AirState.Landing);
            owner.FinishJump();
            SetupAnimation("Jump_Land", 0.3f);
            base.Enter(owner);
            Debug.Log("Jump_Land");
        }

        protected override void OnAnimationEnd(Vagabond owner)
        {
            owner.StateMachine.ChangeState(new VagabondIdleState());
        }
    }

    public class VagabondHardLandingState : AnimationState<Vagabond>
    {
        public override void Enter(Vagabond owner)
        {
            owner.SetAirState(CharacterController.AirState.Landing);
            owner.FinishJump();
            SetupAnimation("Jump_HardLand", 0.9f);
            base.Enter(owner);
            Debug.Log("Hard Landing");
        }

        protected override void OnAnimationEnd(Vagabond owner)
        {
            owner.StateMachine.ChangeState(new VagabondIdleState());
        }
    }
}