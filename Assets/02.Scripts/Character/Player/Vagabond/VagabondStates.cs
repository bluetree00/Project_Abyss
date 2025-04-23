using UnityEngine;
using Game.CharacterStates.CharacterControllerStates;

namespace Game.CharacterStates.VagabondStates
{
    public static class AnimationHelper
    {
        public static bool IsAnimationFinished(Animator anim, string animationName, float endTime = 0.95f, int layer = 0)
        {
            if (anim == null) return false;
            AnimatorStateInfo animState = anim.GetCurrentAnimatorStateInfo(layer);
            return animState.IsName(animationName) && animState.normalizedTime >= endTime;
        }
    }

    public class VagabondIdleState : IdleState<Vagabond> { }

      public class VagabondMoveBlendState : State<Vagabond>
    {
        public override void Enter(Vagabond owner)
        {
            owner.Anim.CrossFade("MoveBlend", 0.1f);
        }

        public override void Execute(Vagabond owner)
        {
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
        private readonly int comboIndex;

        public VagabondComboAttackState(string animName, int comboStep)
        {
            comboIndex = comboStep;
            SetupAnimation(animName);
        }

        public override void Enter(Vagabond owner)
        {
            float endTime = owner.weaponManagerSO.CurrentWeapon.comboEndTimes[comboIndex - 1];
            SetupAnimation(owner.weaponManagerSO.CurrentWeapon.normalAttackAnimations[comboIndex - 1], endTime);
            base.Enter(owner);
        }

        protected override void OnAnimationEnd(Vagabond owner)
        {
            if (comboIndex == owner.weaponManagerSO.CurrentWeapon.maxComboCount)
            {
                owner.CharacterData.attackComboStep = 0;
                owner.CharacterData.comboTimer = 0;
                owner.StateMachine.ChangeState(new VagabondIdleState());
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