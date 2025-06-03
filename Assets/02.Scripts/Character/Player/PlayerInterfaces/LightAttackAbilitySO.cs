using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public abstract class LightAttackAbilitySO : ScriptableObject, ILightAttackAbility<CharacterBase>
{
    public abstract void LightAttack(CharacterBase controller);
}
