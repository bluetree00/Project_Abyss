using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

public class BatFSMController : MonsterController
{
    protected Dictionary<MonsterState, IMonsterState> fsmStates;
    protected MonsterState currentStateKey;
    protected IMonsterState currentState;

    protected override async Task InitAsync()
    {
        await base.InitAsync();
        InitializeFSM();
        ChangeState(MonsterState.Idle);
    }

    protected virtual void InitializeFSM()
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

    private void ChangeState(MonsterState newState)
    {
        currentState?.Exit();

        currentStateKey = newState;
        currentState = fsmStates[newState];
        currentState.Enter();
    }

    public override void HandleAI()
    {
        if (currentState == null) return;

        MonsterState nextState = currentState.Update();
        if (nextState != currentStateKey)
        {
            ChangeState(nextState);
        }
    }

    protected override void Update()
    {
        base.Update();
        HandleAI();
    }
}
