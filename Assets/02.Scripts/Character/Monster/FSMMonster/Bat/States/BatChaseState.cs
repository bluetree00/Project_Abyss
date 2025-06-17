using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class BatChaseState : IMonsterState
{
    public void Enter()
    {
        Transform player = Managers.Player.PlayerTransform;
    }

    public void Exit()
    {
   
    }

    public void Init(MonsterController controller)
    {
        
    }

    public void Init(MonsterController controller, IMonsterStateChanger stateChanger)
    {
        
    }

    MonsterController.MonsterState IMonsterState.Update()
    {
        return MonsterController.MonsterState.Attack;
        
    }
}
