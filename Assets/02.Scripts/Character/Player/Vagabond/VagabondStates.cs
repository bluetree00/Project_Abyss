using System.Collections;
using UnityEngine;
using Game.CharacterStates.States;

namespace Game.CharacterStates.VagabondStates
{
    
    public static class AnimationHelper // 애니메이션 도우미 클래스 예시 : if (AnimationHelper.IsAnimationFinished(owner.Anim, "Attack_01")) 후에 체크크
    {
        public static bool IsAnimationFinished(Animator anim, string animationName, float endTime = 0.95f, int layer = 0)
        {
            if (anim == null) return false;

            AnimatorStateInfo animState = anim.GetCurrentAnimatorStateInfo(layer);
            return animState.IsName(animationName) && animState.normalizedTime >= endTime;
        }
    }

    public class VagabondIdleState : State<Vagabond>
    {
        public override void Enter(Vagabond owner)
        {
            owner.Anim.CrossFade("Idle", 0.2f);
        }

        public override void Execute(Vagabond owner)
        {
 
        }

        public override void Exit(Vagabond owner)
        {
            
        }
    }

    // Move

    public class VagabondMoveState : State<Vagabond>
    {
        public override bool BlocksInput => false;
        public override void Enter(Vagabond owner)
        {
            owner.Anim.CrossFade("Moving", 0.2f);
        }

        public override void Execute(Vagabond owner)
        {
            // 이동 처리
            owner.Move(owner.MoveDirection, owner.CharacterData.baseMoveSpeed);
        }

        public override void Exit(Vagabond owner)
        {
            // 상태 빠질 때 필요한 처리 있으면 여기에
        }
    }


    public class VagabondRunState : State<Vagabond>
    {
        public override bool BlocksInput => false;
        public override void Enter(Vagabond owner)
        {
           owner.Anim.CrossFade("Runing", 0.2f);
        }

        public override void Execute(Vagabond owner)
        {
            owner.Move(owner.MoveDirection, owner.CharacterData.baseRunSpeed);
        }

        public override void Exit(Vagabond owner) { }
    }


    // Dodge
    public class VagabondDodgeState : State<Vagabond>
    {
        public override bool BlocksInput => false;

        public override void Enter(Vagabond owner)
        {
            owner.Anim.CrossFade("Dodge", 0.1f);
        }

        public override void Execute(Vagabond owner)
        {
            // 회피 중 처리
        }

        public override void Exit(Vagabond owner)
        {
            // 회피 종료 처리
        }
    }

    // Attack
    public class VagabondAttack_01 : State<Vagabond>
    {
        private bool _blocksInput = false;
        public override bool BlocksInput => _blocksInput;

        public override void Enter(Vagabond owner)
        {
           // _blocksInput = true; // 입력 받기 차단
           _blocksInput = true;
            owner.Anim.CrossFade("NormalAttack_01", 0.1f);
            Debug.Log("NormalAttack_01");
        
      
        }

        public override void Execute(Vagabond owner)
        {
            // 애니메이션 끝났는지 체크
            if (AnimationHelper.IsAnimationFinished(owner.Anim, "NormalAttack_01", 0.6f))
            {
              _blocksInput = false; // 이제 입력 받기 허용
            }
        }
        public override void Exit(Vagabond owner)
        {
            _blocksInput = false; // 상태 종료 시 확실히 입력 허용
        }
    }


    public class VagabondAttack_02 : State<Vagabond>
    {
         private bool _blocksInput = false;
        public override bool BlocksInput => _blocksInput;

       public override void Enter(Vagabond owner)
        {
           // _blocksInput = true; // 입력 받기 차단
           _blocksInput = true;
            owner.Anim.CrossFade("NormalAttack_02", 0.1f);

      
        }

        public override void Execute(Vagabond owner)
        {
            // 애니메이션 끝났는지 체크
            if (AnimationHelper.IsAnimationFinished(owner.Anim, "NormalAttack_02", 0.6f))
            {
              _blocksInput = false; // 이제 입력 받기 허용
            }
        }

        public override void Exit(Vagabond owner) { }
    }

    public class VagabondAttack_03 : State<Vagabond>
    {
        private bool _blocksInput = false;
        public override bool BlocksInput => _blocksInput;

       public override void Enter(Vagabond owner)
        {
           // _blocksInput = true; // 입력 받기 차단
           _blocksInput = true;
            owner.Anim.CrossFade("NormalAttack_03", 0.1f);
        

        }

        public override void Execute(Vagabond owner)
        {
            // 애니메이션 끝났는지 체크
            if (AnimationHelper.IsAnimationFinished(owner.Anim, "NormalAttack_03", 0.8f))
            {
              _blocksInput = false; // 이제 입력 받기 허용
            }
        }

        public override void Exit(Vagabond owner) { }
    }

    public class VagabondChangeWeaponState : State<Vagabond>
{
    public override bool BlocksInput => true;

    public override void Enter(Vagabond owner)
    {
        if (owner.currentWeapon != null && !string.IsNullOrEmpty(owner.currentWeapon.weapon_ChangeWeapon_AnimationName))
        {
            owner.Anim.CrossFade(owner.currentWeapon.weapon_ChangeWeapon_AnimationName, 0.2f);
        }
        else
        {
            Debug.LogWarning("무기 변경 애니메이션 이름이 비어있거나 무기가 없습니다.");
        }
    }

    public override void Execute(Vagabond owner) { }
    public override void Exit(Vagabond owner) { }
}


    // Skill
    public class VagabondSkillState : State<Vagabond>
    {
        public override bool BlocksInput => true;

        public override void Enter(Vagabond owner)
        {
            owner.Anim.CrossFade("NormalSkile_01", 0.1f);
        }

        public override void Execute(Vagabond owner) { }
        public override void Exit(Vagabond owner) { }
    }

    // Ultimate
    public class VagabondUltimateState : State<Vagabond>
    {
        public override bool BlocksInput => true;
        
        public override void Enter(Vagabond owner)
        {
            owner.Anim.CrossFade("UltimateSkile_01", 0.1f);
        }

        public override void Execute(Vagabond owner) { }
        public override void Exit(Vagabond owner) { }
    }
}
