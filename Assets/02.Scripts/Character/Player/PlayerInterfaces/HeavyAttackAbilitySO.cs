using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public abstract class HeavyAttackAbilitySO : ScriptableObject, IHeavyAttackAbility<CharacterBase>
{
    public abstract void HeavyAttackStartCharging(CharacterBase character);
    public abstract void HeavyAttackUpdateCharging(CharacterBase character, float chargeTime);
    public abstract void HeavyAttackReleaseChargedAttack(CharacterBase character, float chargeTime);
    public abstract void HeavyAttackCancelCharging(CharacterBase character);

}
