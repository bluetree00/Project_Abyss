using System;
using UnityEngine;

public class SkeletonKnightIdleState : IMonsterState
{
    private MonsterController controller;
    private IMonsterStateChanger stateChanger;

    private float idleDuration = 3f;
    private float elapsedTime;

    private IMonsterAbility detectAbility;

    public void Init(MonsterController controller, IMonsterStateChanger stateChanger)
    {
        this.controller = controller;
        this.stateChanger = stateChanger;

        var abilitySet = controller.AbilitySet;
        detectAbility = abilitySet.GetAbility<IMonsterAbility>(Define.MonsterAbilityType.Detect);
    }

    public void Enter()
    {
        elapsedTime = 0f;
        controller.animator.CrossFade("MoveBlend", 0.1f);
    }

    public void Exit()
    {
    }

    public MonsterController.MonsterState StateUpdate()
    {
        elapsedTime += Time.deltaTime;

        detectAbility?.Execute();

        if (controller.HasDetectedTarget)
        {
            stateChanger.RequestStateChange(MonsterController.MonsterState.Chase);
            return MonsterController.MonsterState.Chase;
        }

        if (elapsedTime >= idleDuration)
        {
            stateChanger.RequestStateChange(MonsterController.MonsterState.Patrol);
            return MonsterController.MonsterState.Patrol;
        }

        return MonsterController.MonsterState.Idle;
    }
}
