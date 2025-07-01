using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(menuName = "Monster/Abilities/AttackSet")]
public class AttackAbilitySetSO : MonsterAbilitySO
{
    [SerializeField]
    public List<AttackAbilitySO> attackAbilities = new List<AttackAbilitySO>();

    public override Define.MonsterAbilityType MonsterAbilityType => Define.MonsterAbilityType.Attack;

    public override IMonsterAbility CreateAbilityInstance()
    {
        var set = new AttackAbilitySet();

        foreach (var abilitySO in attackAbilities)
        {
            var abilityInstance = abilitySO.CreateAbilityInstance();
            set.AddAttackAbility(abilityInstance);
        }

        return set;
    }
}
