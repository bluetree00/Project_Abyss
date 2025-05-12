using UnityEngine;
using Game.CharacterStates.CharacterControllerStates;

namespace Game.CharacterStates.VagabondStates
{
    public class VagabondIdleState : IdleState<Vagabond>
    {
        public override void Enter(Vagabond owner)
        {
            base.Enter(owner);
            owner.Anim.CrossFade("VagabondIdle", 0.2f);
        }

        public override void Execute(Vagabond owner) { }

        public override void Exit(Vagabond owner)
        {
            base.Exit(owner);
        }
    }

    public class VagabondMoveBlendState : State<Vagabond>
    {
        public override void Enter(Vagabond owner)
        {
            owner.Anim.CrossFade("MoveBlend", 0.1f);
        }

        public override void Execute(Vagabond owner)
        {
            if (!owner.CanProcessInput()) return;

            float moveAmount = owner.MoveDirection.magnitude;
            float targetSpeed = moveAmount > 0 ? (Input.GetKey(KeyCode.LeftShift) ? 1f : 0.5f) : 0f;
            owner.Anim.SetFloat("MoveSpeed", targetSpeed, 0.1f, Time.deltaTime);
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
            int index = Mathf.Clamp(comboIndex, 0, weapon.lightAttackAnimationSetSO.normalAttackAnimations.Length - 1);
            string animName = weapon.lightAttackAnimationSetSO.normalAttackAnimations[index];
            float endTime = weapon.lightAttackAnimationSetSO.comboEndTimes[index];

            SetupAnimation(animName, endTime);
            owner.RotateTowardsMousePosition();
            owner.OnAttackAnimationStart();
            base.Enter(owner);
        }

        public override void Exit(Vagabond owner)
        {
            blocksInput = false;
            owner.OnAttackAnimationEnd();
        }

        protected override void OnAnimationEnd(Vagabond owner)
        {
            owner.OnAttackAnimationEnd();
          
                owner.StateMachine.ChangeState(owner.GetState<VagabondIdleState>());
            
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

                 owner.StateMachine.ChangeState(new VagabondIdleState()); //임시
            }
            else
            {
                Debug.LogWarning("무기 변경 애니메이션 이름이 비어있거나 무기가 없습니다.");

                 owner.StateMachine.ChangeState(new VagabondIdleState()); //임시
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
            owner.JumpAbility.Jump(owner);
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

        public class VagabondChargeStartState : AnimationState<Vagabond>
    {
        public override void Enter(Vagabond owner)
        {
            owner.HeavyAttackAbility.HeavyAttackStartCharging(owner);
            SetupAnimation("Heavycharge", 0.3f);
            base.Enter(owner);
        }

        protected override void OnAnimationEnd(Vagabond owner)
        {
            owner.StateMachine.ChangeState(owner.GetState<VagabondChargeHoldingState>());
        }
    }

    public class VagabondChargeHoldingState : State<Vagabond>
    {
        private bool blocksInput = true;
        public override bool BlocksInput => blocksInput;

        public override void Enter(Vagabond owner)
        {
            Debug.Log("차지 공격 유지 상태 진입");
            
        }

        public override void Execute(Vagabond owner)
        {
            owner.HeavyAttackAbility.HeavyAttackUpdateCharging(owner, owner.ChargeTime);

           
                if (owner.ChargeTime >= owner.HeavyAttackAbility.MinChargeTime)
                {
                    owner.StateMachine.ChangeState(owner.GetState<VagabondChargedAttackState>());
                }
                else
                    owner.StateMachine.ChangeState(owner.GetState<VagabondChargeCancelState>());
            
        }

        public override void Exit(Vagabond owner)
        {
            blocksInput = false;  // 차지 상태 종료 후 입력 차단 해제
            Debug.Log("차지 공격 유지 상태 종료");
        }
    }


    public class VagabondChargedAttackState : AnimationState<Vagabond>
    {
        private bool blocksInput = true;
        public override bool BlocksInput => blocksInput;

        public override void Enter(Vagabond owner)
        {
            blocksInput = true;  // 공격 중 입력 차단
            owner.OnAttackAnimationStart();

            owner.HeavyAttackAbility.HeavyAttackReleaseChargedAttack(owner, owner.ChargeTime);

            var weapon = owner.weaponManagerSO.CurrentWeapon;
            string animName = weapon?.heavyAttackSet?.attackClip?.name ?? "HeavyAttack";

            SetupAnimation(animName, 0.7f);
            base.Enter(owner);
        }

        protected override void OnAnimationEnd(Vagabond owner)
        {
            blocksInput = false;  // 공격 종료 후 입력 차단 해제
            owner.StateMachine.ChangeState(owner.GetState<VagabondIdleState>());
            owner.OnAttackAnimationEnd();
        }
    }

    public class VagabondChargeCancelState : AnimationState<Vagabond>
    {
        public override void Enter(Vagabond owner)
        {
            owner.HeavyAttackAbility.HeavyAttackCancelCharging(owner);
            SetupAnimation("HeavyAttackEnd", 0.4f);
            base.Enter(owner);
        }

        protected override void OnAnimationEnd(Vagabond owner)
        {
            owner.OnAttackAnimationEnd();
            owner.StateMachine.ChangeState(owner.GetState<VagabondIdleState>());
        }
    }
}
    