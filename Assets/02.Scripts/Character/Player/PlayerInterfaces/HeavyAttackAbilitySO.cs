using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public abstract class HeavyAttackAbilitySO : ScriptableObject, IHeavyAttackAbility<PlayerCharacter>
{
    public abstract void HeavyAttackStartCharging(PlayerCharacter character);
    public abstract void HeavyAttackUpdateCharging(PlayerCharacter character, float chargeTime);
    public abstract void HeavyAttackReleaseChargedAttack(PlayerCharacter character, float chargeTime);
    public abstract void HeavyAttackCancelCharging(PlayerCharacter character);

}
