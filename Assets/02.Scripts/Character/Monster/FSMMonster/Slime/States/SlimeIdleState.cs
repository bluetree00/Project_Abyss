using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class SlimeIdleState : IMonsterState
{
    private MonsterController slime;
    private float idleTime;
    private float elapsed;

    public void Init(MonsterController controller)
    {
        slime = controller;
    }

    public void Enter()
    {
        slime.animator.Play("Idle");
        idleTime = Random.Range(1f, 3f);
        elapsed = 0f;
    }

    public MonsterController.MonsterState Update()
    {
        elapsed += Time.deltaTime;

        // if (Vector3.Distance(slime.transform.position, slime.target.position) < slime.detectionRange)
        // {
        //     return MonsterController.MonsterState.Chase;
        // }

        // if (elapsed >= idleTime)
        // {
        //     return MonsterController.MonsterState.Patrol;
        // }

        return MonsterController.MonsterState.Idle;
    }

    public void Exit() { }

    public void Init(MonsterController controller, IMonsterStateChanger stateChanger)
    {
        throw new System.NotImplementedException();
    }
}
