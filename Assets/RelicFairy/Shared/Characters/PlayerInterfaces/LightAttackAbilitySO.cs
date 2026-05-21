using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public abstract class LightAttackAbilitySO : ScriptableObject, ILightAttackAbility<PlayerController>
{
    public abstract void LightAttack(PlayerController controller);

    public abstract void SpawnEffect(PlayerController controller, Vector3 forwardOffset, Vector3? additionalRotation = null);
}
