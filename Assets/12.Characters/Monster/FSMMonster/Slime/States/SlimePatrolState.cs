using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class SlimePatrolState : IMonsterState
{
    private MonsterController controller;
    private IMonsterStateChanger stateChanger;

    private IMonsterAbility patrolAbility;
    private IMonsterAbility detectAbility;

    public void Init(MonsterController controller, IMonsterStateChanger stateChanger)
    {
        this.controller = controller;
        this.stateChanger = stateChanger;

        patrolAbility = controller.AbilitySet.GetAbility<IMonsterAbility>(Define.MonsterAbilityType.Patrol);
        detectAbility = controller.AbilitySet.GetAbility<IMonsterAbility>(Define.MonsterAbilityType.Detect);
    }

    public void Enter()
    {
        controller.animator.CrossFade("MoveBlend", 0.1f);
    }

    public MonsterController.MonsterState StateUpdate()
    {
        patrolAbility?.Execute();
        detectAbility?.Execute();

        if (controller.HasDetectedTarget)
        {
            Debug.Log("[SlimePatrolState] Target Detected!");
            stateChanger.RequestStateChange(MonsterController.MonsterState.Chase);
            return MonsterController.MonsterState.Chase;
        }

        return MonsterController.MonsterState.Patrol;
    }

    public void Exit()
    {
        controller.StopMoving();
    }
}
