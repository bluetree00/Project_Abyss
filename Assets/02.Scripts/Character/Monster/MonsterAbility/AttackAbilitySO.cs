using UnityEngine;

[CreateAssetMenu(menuName = "Monster/Abilities/Attack")]
public class AttackAbilitySO : MonsterAbilitySO
{
    [SerializeField] private float attackRange = 2f;
    [SerializeField] private float cooldownTime = 1.5f;

    public override Define.MonsterAbilityType MonsterAbilityType => Define.MonsterAbilityType.Attack;

    public void SetAttackRange(float r)
    {
        attackRange = r;
    }

    public void SetCooldownTime(float t)
    {
        cooldownTime = t;
    }

    public override IMonsterAbility CreateAbilityInstance()
    {
        return new AttackAbility(attackRange, cooldownTime);
    }
}
