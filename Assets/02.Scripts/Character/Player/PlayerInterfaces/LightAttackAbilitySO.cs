using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public abstract class LightAttackAbilitySO : ScriptableObject, ILightAttackAbility<PlayerCharacter>
{
    public abstract void LightAttack(PlayerCharacter controller);

    public abstract void SpawnEffect(PlayerCharacter controller, Vector3 forwardOffset, Vector3? additionalRotation = null);
}
