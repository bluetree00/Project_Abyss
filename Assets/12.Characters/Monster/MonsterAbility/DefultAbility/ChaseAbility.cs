using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class ChaseAbility : IMonsterAbility
{
    private float speed;
    private float attackRange;
    private MonsterController owner;

    public Define.MonsterAbilityType Type => Define.MonsterAbilityType.Chase;

    public ChaseAbility(float speed, float attackRange)
    {
        this.speed = speed;
        this.attackRange = attackRange;
    }

    public void Init(MonsterController owner)
    {
        this.owner = owner;
    }

    public void Execute()
    {
        Transform target = owner.playerTarget;
        if (target == null)
            return;

        owner.MoveTo(target.position);

        float distance = Vector3.Distance(owner.transform.position, target.position);
        owner.SetInAttackRange(distance <= attackRange); // FSM이 감지하도록
    }
}
