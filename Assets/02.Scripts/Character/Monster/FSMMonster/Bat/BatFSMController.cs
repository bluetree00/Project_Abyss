using System.Collections.Generic;
using UnityEngine;
using System.Threading.Tasks;

public class BatFSMController : MonsterController
{
    private Dictionary<MonsterState, IMonsterState> fsmStates;
    private MonsterState currentStateKey;
    private IMonsterState currentState;

    public System.Action<MonsterState> RequestStateChange;

    protected override async Task InitAsync()
    {
        RequestStateChange = ChangeState;
        await base.InitAsync();
        InitializeFSM();
        ChangeState(MonsterState.Idle);
    }

    private void InitializeFSM()
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
            state.Init(this, RequestStateChange);
        }
    }

    private void ChangeState(MonsterState newState)
    {
        if (currentState != null)
            currentState.Exit();

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

    // protected override void Update()
    // {
    //     base.Update();
    //     HandleAI();
    // }
}
