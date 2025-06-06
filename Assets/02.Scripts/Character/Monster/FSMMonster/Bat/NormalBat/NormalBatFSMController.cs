using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class NormalBatFSMController : BatFSMController
{
    protected override void InitializeFSM()
    {
        fsmStates = new Dictionary<MonsterState, IMonsterState>
        {
            { MonsterState.Idle, new BatIdleState() },
            { MonsterState.Patrol, new BatPatrolState() },
            { MonsterState.Chase, new BatChaseState() },
            { MonsterState.Attack, new BatAttackState() },
            { MonsterState.Die, new BatDieState() }
        };

        foreach (var state in fsmStates.Values)
        {
            state.Init(this);
        }
    }
}
