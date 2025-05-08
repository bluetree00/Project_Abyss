using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public abstract class HeavyAttackAbilitySO : ScriptableObject, IHeavyAttackAbility<CharacterController>
{
    public abstract void HeavyAttack(CharacterController controller);
}
