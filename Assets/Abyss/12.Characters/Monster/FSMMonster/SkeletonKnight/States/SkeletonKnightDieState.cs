using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class SkeletonKnightDieState : IMonsterState
{
    private MonsterController controller;
    private IMonsterStateChanger stateChanger;

    public void Init(MonsterController controller, IMonsterStateChanger stateChanger)
    {
        this.controller = controller;
        this.stateChanger = stateChanger;
    }

    public void Enter()
    {
        controller.animator.SetTrigger("Die");
    }

    public MonsterController.MonsterState StateUpdate()
    {
        return MonsterController.MonsterState.Die;
    }

    public void Exit()
    {
    }
}
