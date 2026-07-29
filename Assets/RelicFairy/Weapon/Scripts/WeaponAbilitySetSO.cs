using System;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(menuName = "Game/AbilitySetSO")]
public class WeaponAbilitySetSO : ScriptableObject
{
    [Serializable]
    public class AbilityGroup
    {
        public WeaponActionType actionType;
        public List<WeaponAbilitySO> abilities = new List<WeaponAbilitySO>();

        public WeaponAbilitySO GetAbilityForComboIndex(int comboIndex)
        {
            if (abilities == null || abilities.Count == 0) return null;
            int idx = Mathf.Clamp(comboIndex, 0, abilities.Count - 1);
            return abilities[idx];
        }
    }

    public List<AbilityGroup> groups = new List<AbilityGroup>();

    private Dictionary<WeaponActionType, AbilityGroup> _groupCache;

    private void OnEnable() => RebuildCache();

    private void RebuildCache()
    {
        _groupCache = new Dictionary<WeaponActionType, AbilityGroup>();
        if (groups == null) return;
        foreach (var g in groups)
            _groupCache.TryAdd(g.actionType, g);
    }

    public WeaponAbilitySO GetAbility(WeaponActionType actionType, int comboIndex)
    {
        if (_groupCache == null) RebuildCache();
        return _groupCache.TryGetValue(actionType, out var g) ? g.GetAbilityForComboIndex(comboIndex) : null;
    }
}
