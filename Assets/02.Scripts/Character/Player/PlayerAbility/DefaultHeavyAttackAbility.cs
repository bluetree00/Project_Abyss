using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(menuName = "Weapon/Abilities/LightAttack")]
public class DefaultHeavyAttackAbility : ScriptableObject, IHeavyAttackAbility<CharacterController>
{
    public void HeavyAttack(CharacterController controller)
    {
        throw new System.NotImplementedException();
    }
}
