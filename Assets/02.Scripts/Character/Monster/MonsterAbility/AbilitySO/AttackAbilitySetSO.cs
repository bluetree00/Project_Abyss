using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(menuName = "Monster/Abilities/AttackSet")]
public class AttackAbilitySetSO : MonsterAbilitySO
{
    [SerializeField]
    public List<AttackAbilitySO> attackAbilities = new List<AttackAbilitySO>();

    [SerializeField] private float attackRange = 2f;
    [SerializeField] private float cooldownTime = 1.5f;

    public override Define.MonsterAbilityType MonsterAbilityType => Define.MonsterAbilityType.Attack;

    public void SetAttackRange(float r) => attackRange = r;
    public void SetCooldownTime(float t) => cooldownTime = t;

    public override IMonsterAbility CreateAbilityInstance()
    {
        var set = new AttackAbilitySet(attackRange, cooldownTime);

        foreach (var abilitySO in attackAbilities)
        {
            var abilityInstance = abilitySO.CreateAbilityInstance();
            set.AddAttackAbility(abilityInstance);
        }

        return set;
    }
}
