using System.Collections;
using System.Collections.Generic;
using UnityEngine;

#region Ability (attack) SO
[CreateAssetMenu(menuName = "Game/AttackAbilitySO")]
public class WeaponAttackAbilitySO : ScriptableObject
{
public string abilityId;
public float damage = 10f;
public float knockback = 0f;
public bool applyPlayerMotion = false; // e.g. dash/push the player
public float forwardDistance = 0f;
public float forwardDuration = 0.1f;
// other gameplay params
}


[CreateAssetMenu(menuName = "Game/AbilitySetSO")]
public class WeaponAbilitySetSO : ScriptableObject
{
public List<WeaponAttackAbilitySO> abilities = new List<WeaponAttackAbilitySO>();


public WeaponAttackAbilitySO GetAbilityByIndex(int idx)
{
    if (idx >= 0 && idx < abilities.Count) return abilities[idx];
    return null;
}
}
#endregion