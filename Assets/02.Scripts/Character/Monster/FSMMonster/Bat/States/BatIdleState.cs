using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class BatIdleState : IMonsterState
{
    public void Enter()
    {
       
    }

    public void Exit()
    {
        throw new System.NotImplementedException();
    }

    public void Init(MonsterController controller, IMonsterStateChanger stateChanger)
    {
        throw new NotImplementedException();
    }


    MonsterController.MonsterState IMonsterState.Update()
    {
        throw new System.NotImplementedException();
    }
}
