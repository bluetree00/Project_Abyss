using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public abstract class LightAttackAbilitySO : ScriptableObject, ILightAttackAbility<CharacterController>
{
    public abstract void LightAttack(CharacterController controller);
}
