using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public abstract class HeavyAttackAbilitySO : ScriptableObject, IHeavyAttackAbility<CharacterController>
{
    public abstract void HeavyAttackStartCharging(CharacterController character);
    public abstract void HeavyAttackUpdateCharging(CharacterController character, float chargeTime);
    public abstract void HeavyAttackReleaseChargedAttack(CharacterController character, float chargeTime);
    public abstract void HeavyAttackCancelCharging(CharacterController character);

     public abstract float MinChargeTime { get; }
    public abstract float MaxChargeTime { get; }
}
