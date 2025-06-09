using UnityEngine;
using System.Collections.Generic;

[CreateAssetMenu(menuName = "Monster/Ability Set")]
public class MonsterAbilitySetSO : ScriptableObject
{
    [SerializeField] private List<MonsterAbilitySO> abilityList;

    private Dictionary<Define.MonsterAbilityType, IMonsterAbility> runtimeAbilities = new();

    public void InitAbilities(MonsterController controller)
    {
        runtimeAbilities.Clear();

        foreach (var abilitySO in abilityList)
        {
            var ability = abilitySO.CreateAbilityInstance();
            ability.Init(controller);
            runtimeAbilities.Add(abilitySO.Type, ability);
        }
    }

    public T GetAbility<T>(Define.MonsterAbilityType type) where T : class, IMonsterAbility
    {
        if (runtimeAbilities.TryGetValue(type, out var ability))
            return ability as T;

        return null;
    }

    public bool HasAbility(Define.MonsterAbilityType type)
    {
        return runtimeAbilities.ContainsKey(type);
    }
}
