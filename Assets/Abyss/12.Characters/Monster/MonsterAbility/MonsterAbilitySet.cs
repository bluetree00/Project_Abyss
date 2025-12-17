using System.Collections.Generic;
using UnityEngine;

public class MonsterAbilitySet
{
    private Dictionary<Define.MonsterAbilityType, IMonsterAbility> abilityMap = new();

    public MonsterAbilitySet(MonsterAbilitySetSO abilitySetSO, MonsterController owner)
    {
        foreach (var abilitySO in abilitySetSO.abilities)
        {
            var instance = abilitySO.ReturnAbilityInstance();
            instance.Init(owner);
            abilityMap[abilitySO.MonsterAbilityType] = instance;
        }
    }

    public T GetAbility<T>(Define.MonsterAbilityType type) where T : class, IMonsterAbility
    {
        if (abilityMap.TryGetValue(type, out var ability))
            return ability as T;

        return null;
    }

    public bool TryGetAbility<T>(Define.MonsterAbilityType type, out T result) where T : class, IMonsterAbility
    {
        if (abilityMap.TryGetValue(type, out var ability))
        {
            result = ability as T;
            return result != null;
        }

        result = null;
        return false;
    }
}
