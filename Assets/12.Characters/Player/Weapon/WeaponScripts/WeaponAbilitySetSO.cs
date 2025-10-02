using System;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(menuName = "Game/AbilitySetSO")]
public class WeaponAbilitySetSO : ScriptableObject
{
    [Serializable]
    public class AbilityGroup
    {
        public WeaponActionType actionType;           // Light, Heavy, QSkill, etc.
        public List<WeaponAbilitySO> abilities = new List<WeaponAbilitySO>();

        public WeaponAbilitySO GetAbilityForComboIndex(int comboIndex)
        {
            if (abilities == null || abilities.Count == 0) return null;
            int idx = Mathf.Clamp(comboIndex - 1, 0, abilities.Count - 1);
            return abilities[idx];
        }
    }

    public List<AbilityGroup> groups = new List<AbilityGroup>();

    public WeaponAbilitySO GetAbility(WeaponActionType actionType, int comboIndex)
    {
        foreach (var g in groups)
        {
            if (g.actionType == actionType)
                return g.GetAbilityForComboIndex(comboIndex);
        }
        return null;
    }
}
