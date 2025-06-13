using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class SlimeChaseState : IMonsterState
{
    private MonsterController slime;

    public void Init(MonsterController controller)
    {
        slime = controller;
    }

    public void Enter()
    {
        slime.animator.Play("Run");
    }

    public MonsterController.MonsterState Update()
    {
        // if (Vector3.Distance(slime.transform.position, slime.target.position) > slime.detectionRange)
        // {
        //     return MonsterController.MonsterState.Idle;
        // }

        // if (Vector3.Distance(slime.transform.position, slime.target.position) <= slime.attackRange)
        // {
        //     return MonsterController.MonsterState.Attack;
        // }

        // slime.agent.SetDestination(slime.target.position);
        return MonsterController.MonsterState.Chase;
    }

    public void Exit() { }

    public void Init(MonsterController controller, IMonsterStateChanger stateChanger)
    {
        throw new System.NotImplementedException();
    }
}

