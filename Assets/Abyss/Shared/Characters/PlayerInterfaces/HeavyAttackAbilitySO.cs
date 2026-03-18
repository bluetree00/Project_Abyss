using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public abstract class HeavyAttackAbilitySO : ScriptableObject, IHeavyAttackAbility<PlayerController>
{
    public abstract void HeavyAttackStartCharging(PlayerController character);
    public abstract void HeavyAttackUpdateCharging(PlayerController character, float chargeTime);
    public abstract void HeavyAttackReleaseChargedAttack(PlayerController character, float chargeTime);
    public abstract void HeavyAttackCancelCharging(PlayerController character);

}
