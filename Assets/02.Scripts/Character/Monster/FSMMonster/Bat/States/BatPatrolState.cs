using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class BatPatrolState : IMonsterState
{
    private MonsterController controller;
    private IMonsterStateChanger stateChanger;

    private Vector3 patrolTarget;

    public void Init(MonsterController controller, IMonsterStateChanger stateChanger)
    {
        this.controller = controller;
        this.stateChanger = stateChanger;
    }

    public void Enter()
    {
        // 예시: 주변 임의의 위치로 순찰 이동
        patrolTarget = controller.transform.position + Random.insideUnitSphere * 5f;
        patrolTarget.y = controller.transform.position.y;

        controller.MoveTo(patrolTarget);
    }

    public void Exit()
    {
        controller.StopMoving();
    }

    public MonsterController.MonsterState Update()
    {
        Debug.Log("BatPatrolState Update");
        // 탐지되면 추적 상태로 전환
        if (controller.HasDetectedTarget)
            return MonsterController.MonsterState.Chase;

        if (!controller.agent.pathPending && controller.agent.remainingDistance < 0.5f)
        {
            // 도착 후 다음 상태 또는 위치 갱신
            return MonsterController.MonsterState.Idle;
        }

        return MonsterController.MonsterState.Patrol;
    }
}
