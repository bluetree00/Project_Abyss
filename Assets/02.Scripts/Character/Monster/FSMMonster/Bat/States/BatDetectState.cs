using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class BatDetectState : IMonsterState
{
    private MonsterController monster;
    private IMonsterStateChanger stateChanger;

    public void Init(MonsterController controller, IMonsterStateChanger changer)
    {
        monster = controller;
        stateChanger = changer;
    }

    public void Enter()
    {
        Debug.Log("Entered Detect");
    }

    public void Exit()
    {
        Debug.Log("Exit Detect");
    }

    public void Update()
    {
        // if (PlayerInRange())
        // {
        //     stateChanger.RequestStateChange(MonsterController.MonsterState.Chase);
        // }
    }

    // private bool PlayerInRange()
    // {
    //     // 감지 로직
    //    // return Vector3.Distance(monster.transform.position, Player.Instance.transform.position) < monster.detectionRange;
    // }

    public void Init(MonsterController controller, Action<MonsterController.MonsterState> onStateChange)
    {
        throw new NotImplementedException();
    }

    MonsterController.MonsterState IMonsterState.Update()
    {
        throw new NotImplementedException();
    }
}
