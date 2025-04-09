using System.Collections;
using System.Collections.Generic;
using Game.CharacterStates;
using Game.CharacterStates.CommonStates;
using Game.CharacterStates.StateMachine;
using UnityEngine;

[CreateAssetMenu(menuName = "Weapons/Common/Sword", fileName = "Common_Sword_Data")]
public class CommonSwordData : WeaponData
{
    public override void QSkill()
    {
        
    }

    public override void ESkill()
    {
        
    }

    public override StateMachine<CharacterController> CreateAttackStateMachine(CharacterController owner)
    {
        var sm = new AttackStateMachine<CharacterController>();
        sm.Setup(owner, new GenericComboAttackState<CharacterController>(sm, 0));
        return sm;
    }
}
