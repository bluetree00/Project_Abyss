using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class SlimeDieState : IMonsterState
{
    private MonsterController slime;
    private bool isDead;

    public void Init(MonsterController controller)
    {
        slime = controller;
    }

    public void Enter()
    {
        slime.agent.isStopped = true;
        slime.animator.Play("Die");
        isDead = true;
    }

    public MonsterController.MonsterState Update()
    {
        // 죽은 상태면 더 이상 상태 전이 없음
        return MonsterController.MonsterState.Die;
    }

    public void Exit() { }

    public void Init(MonsterController controller, IMonsterStateChanger stateChanger)
    {
        throw new System.NotImplementedException();
    }
}
