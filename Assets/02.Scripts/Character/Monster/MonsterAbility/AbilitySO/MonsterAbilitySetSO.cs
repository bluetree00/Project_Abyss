using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(menuName = "Monster/AbilitySet")]
public class MonsterAbilitySetSO : ScriptableObject
{
    [SerializeReference]
    public List<MonsterAbilitySO> abilities = new();

    // 기존 방식은 유지하되, 런타임용 AbilitySet을 만드는 메서드 추가
    public MonsterAbilitySet CreateRuntimeSet(MonsterController owner)
    {
        return new MonsterAbilitySet(this, owner);
    }
}
