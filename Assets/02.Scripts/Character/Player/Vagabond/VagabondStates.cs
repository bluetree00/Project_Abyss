using System.Collections;
using UnityEngine;
using Game.CharacterStates.States;

namespace Game.CharacterStates.VagabondStates
{
    // Idle
    public class VagabondIdleState : IdleState<Vagabond>
    {
        public override void Enter(Vagabond owner)
        {
            base.Enter(owner);
            // 베가본드 전용 Idle 로직
        }

        public override void Execute(Vagabond owner)
        {
            base.Execute(owner);
        }

        public override void Exit(Vagabond owner)
        {
            base.Exit(owner);
        }
    }

    // Move
    public class VagabondMoveState : MoveState<Vagabond>
    {
        public override void Enter(Vagabond owner)
        {
            base.Enter(owner);
        }

        public override void Execute(Vagabond owner)
        {
            base.Execute(owner);
        }

        public override void Exit(Vagabond owner)
        {
            base.Exit(owner);
        }
    }

    // Dodge
    public class VagabondDodgeState : State<Vagabond>
    {
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
        public override void Enter(Vagabond owner)
        {
            owner.NormalAttack(1);
        }

        public override void Execute(Vagabond owner) { }
        public override void Exit(Vagabond owner) { }
    }

    public class VagabondAttack_02 : State<Vagabond>
    {
        public override void Enter(Vagabond owner)
        {
            owner.NormalAttack(2);
        }

        public override void Execute(Vagabond owner) { }
        public override void Exit(Vagabond owner) { }
    }

    public class VagabondAttack_03 : State<Vagabond>
    {
        public override void Enter(Vagabond owner)
        {
            owner.NormalAttack(3);
        }

        public override void Execute(Vagabond owner) { }
        public override void Exit(Vagabond owner) { }
    }

    // Skill
    public class VagabondSkillState : State<Vagabond>
    {
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
        public override void Enter(Vagabond owner)
        {
            owner.Anim.CrossFade("UltimateSkile_01", 0.1f);
        }

        public override void Execute(Vagabond owner) { }
        public override void Exit(Vagabond owner) { }
    }
}
