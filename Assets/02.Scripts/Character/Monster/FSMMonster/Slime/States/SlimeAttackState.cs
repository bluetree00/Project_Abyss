using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class SlimeAttackState : IMonsterState
{
    private MonsterController slime;
    private float attackCooldown = 2f;
    private float lastAttackTime;

    public void Init(MonsterController controller)
    {
        slime = controller;
    }

    public void Enter()
    {
        slime.agent.ResetPath();
        slime.animator.Play("Attack");
        lastAttackTime = Time.time;
    }

    public MonsterController.MonsterState Update()
    {
        // if (Vector3.Distance(slime.transform.position, slime.target.position) > slime.attackRange)
        // {
        //     return MonsterController.MonsterState.Chase;
        // }

        // if (Time.time - lastAttackTime >= attackCooldown)
        // {
        //     slime.animator.Play("Attack");
        //     lastAttackTime = Time.time;
        // }

        return MonsterController.MonsterState.Attack;
    }

    public void Exit() { }

    public void Init(MonsterController controller, IMonsterStateChanger stateChanger)
    {
        throw new System.NotImplementedException();
    }
}
