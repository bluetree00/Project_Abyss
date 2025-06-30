using UnityEngine;
using System.Collections.Generic;

[CreateAssetMenu(menuName = "Monster/AbilitySet")]
public class MonsterAbilitySetSO : ScriptableObject
{
    [SerializeReference]
    public List<MonsterAbilitySO> abilities = new();

    public void InitAbilities(MonsterController owner)
    {
        foreach (var ability in abilities)
            ability.CreateAbilityInstance().Init(owner);
    }

    public T GetAbility<T>(Define.MonsterAbilityType type) where T : class, IMonsterAbility
    {
        var so = abilities.Find(a => a.MonsterAbilityType == type);
        return so?.CreateAbilityInstance() as T;
    }
}
