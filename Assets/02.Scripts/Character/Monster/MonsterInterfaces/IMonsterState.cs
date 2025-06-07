using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public interface IMonsterState
{
    void Init(MonsterController controller, IMonsterStateChanger stateChanger);
    void Enter();
    MonsterController.MonsterState Update();
    void Exit();
}
