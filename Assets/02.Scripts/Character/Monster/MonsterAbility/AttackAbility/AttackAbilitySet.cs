using System.Collections.Generic;
using UnityEngine;

public class AttackAbilitySet : IMonsterAbility
{
    private List<IMonsterAbility> attackAbilities = new();
    private MonsterController owner;

    private float attackRange;
    private float cooldownTime;
    private float lastAttackTime = Mathf.NegativeInfinity;

    public Define.MonsterAbilityType Type => Define.MonsterAbilityType.Attack;

    public AttackAbilitySet(float attackRange, float cooldownTime)
    {
        this.attackRange = attackRange;
        this.cooldownTime = cooldownTime;
    }

    public void Init(MonsterController owner)
    {
        this.owner = owner;
        foreach (var ability in attackAbilities)
        {
            ability.Init(owner);
        }
    }

    public void AddAttackAbility(IMonsterAbility ability)
    {
        attackAbilities.Add(ability);
        if (owner != null)
            ability.Init(owner);
    }

    public void Execute()
    {
        if (!CanAttack())
            return;

        Transform target = owner.playerTarget;
        if (target == null)
            return;

        float distance = Vector3.Distance(owner.transform.position, target.position);
        if (distance <= attackRange)
        {
            lastAttackTime = Time.time;
            owner.SetAttack(true);

            var selectedAbility = SelectAttackAbility();
            selectedAbility?.Execute();
        }
    }

    private IMonsterAbility SelectAttackAbility()
    {
        if (attackAbilities.Count == 0)
            return null;

        return attackAbilities[Random.Range(0, attackAbilities.Count)];
    }

    public bool CanAttack()
    {
        return Time.time >= lastAttackTime + cooldownTime;
    }
}
