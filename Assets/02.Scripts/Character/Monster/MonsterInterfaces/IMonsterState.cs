using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public interface IMonsterState
{
    void Init(MonsterController controller, System.Action<MonsterController.MonsterState> onStateChange);
    void Enter();
    MonsterController.MonsterState Update();
    void Exit();
}
