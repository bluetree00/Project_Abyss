using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class SlimePatrolState : IMonsterState
{
    private MonsterController slime;
    private Vector3 patrolTarget;

    public void Init(MonsterController controller)
    {
        slime = controller;
    }

    public void Enter()
    {
        slime.animator.Play("Walk");
        patrolTarget = slime.transform.position + Random.insideUnitSphere * 5f;
        patrolTarget.y = slime.transform.position.y;

        slime.agent.SetDestination(patrolTarget);
    }

    public MonsterController.MonsterState Update()
    {
        // if (Vector3.Distance(slime.transform.position, slime.target.position) < slime.detectionRange)
        // {
        //     return MonsterController.MonsterState.Chase;
        // }

        // if (!slime.agent.pathPending && slime.agent.remainingDistance <= slime.agent.stoppingDistance)
        // {
        //     return MonsterController.MonsterState.Idle;
        // }

        return MonsterController.MonsterState.Patrol;
    }

    public void Exit() { }
}
