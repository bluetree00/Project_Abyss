using System.Collections;
using System.Collections.Generic;
using UnityEngine;

using Game.CharacterStates;
using Game.CharacterStates.CharacterControllerStates;
using Game.CharacterStates.StateMachine;

namespace Game.CharacterStates.CommonStates
{
    public class GenericComboAttackState<T> : AnimationState<T> where T : CharacterController
{
    private readonly AttackStateMachine<T> machine;
    private readonly int comboIndex;

    public GenericComboAttackState(AttackStateMachine<T> machine, int comboIndex)
    {
        this.machine = machine;
        this.comboIndex = comboIndex;
    }

    public override void Enter(T owner)
    {
        var weapon = owner.currentWeapon;

        if (comboIndex >= weapon.normalAttackAnimations.Length)
        {
            machine.ChangeState(new IdleState<T>());
            return;
        }

        string animName = weapon.normalAttackAnimations[comboIndex];
        float endTime = weapon.comboEndTimes[comboIndex];

        SetupAnimation(animName, endTime);
        base.Enter(owner);
    }

    protected override void OnAnimationEnd(T owner)
    {
        if (machine.NextComboQueued && comboIndex + 1 < owner.currentWeapon.maxComboCount)
        {
            machine.ResetQueue();
            machine.ChangeState(new GenericComboAttackState<T>(machine, comboIndex + 1));
        }
        else
        {
            owner.CharacterData.attackComboStep = 0;
            owner.CharacterData.comboTimer = 0;
            machine.ChangeState(new IdleState<T>());
        }
    }
}

}
