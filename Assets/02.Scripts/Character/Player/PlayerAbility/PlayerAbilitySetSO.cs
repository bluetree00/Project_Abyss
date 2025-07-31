using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(menuName = "Abilities/Player/AbilitySet")]
public class PlayerAbilitySetSO : ScriptableObject
{
    public DefaultMoveAbility moveAbility;
    public DefaultDodgeAbility dodgeAbility;
    // 필요하면 LightAttack, HeavyAttack, Jump 등도 추가
}
