using System;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(menuName = "Game/WeaponEffectPackageSO (Simplified)")]
public class WeaponEffectPackageSO : ScriptableObject
{
    [Serializable]
    public class ActionEntry
    {
        [Header("Action Identity")]
        public WeaponAnimGroup group = WeaponAnimGroup.Ground;
        public WeaponActionType actionType;

        [Header("Effect List")]
        [Tooltip("해당 액션에서 사용 가능한 이펙트 리스트")]
        public List<WeaponEffectSO> effects = new List<WeaponEffectSO>();
    }

    [Header("Weapon Effect Package")]
    public List<ActionEntry> actions = new List<ActionEntry>();

    // 런타임 캐시: (Group, ActionType) -> List<EffectSO>
    private Dictionary<(WeaponAnimGroup, WeaponActionType), List<WeaponEffectSO>> _entryMap;

    private void OnEnable()
    {
        _entryMap = new Dictionary<(WeaponAnimGroup, WeaponActionType), List<WeaponEffectSO>>();

        foreach (var action in actions)
        {
            var key = (action.group, action.actionType);
            if (!_entryMap.ContainsKey(key))
            {
                _entryMap.Add(key, new List<WeaponEffectSO>(action.effects));
            }
        }
    }

    /// <summary>
    /// 런타임 조회: Group + ActionType에 해당하는 이펙트 리스트 반환
    /// </summary>
    public List<WeaponEffectSO> GetEffects(WeaponAnimGroup group, WeaponActionType action)
    {
        if (_entryMap == null) OnEnable();
        _entryMap.TryGetValue((group, action), out var effectList);
        return effectList ?? new List<WeaponEffectSO>();
    }
}
