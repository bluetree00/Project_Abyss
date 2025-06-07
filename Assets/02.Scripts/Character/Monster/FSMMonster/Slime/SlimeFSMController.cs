using UnityEngine;
using System.Collections.Generic;
using System.Threading.Tasks;

public class SlimeFSMController : MonsterController
{
    private Dictionary<MonsterState, IMonsterState> fsmStates;
    private MonsterState currentStateKey;
    private IMonsterState currentState;

    protected override async Task InitAsync()
    {
        await base.InitAsync();

        InitializeFSM();
        ChangeState(MonsterState.Idle);
    }

    private void InitializeFSM()
    {
        fsmStates = new Dictionary<MonsterState, IMonsterState>
        {
            { MonsterState.Idle, new SlimeIdleState() },
            { MonsterState.Patrol, new SlimePatrolState() },
            { MonsterState.Chase, new SlimeChaseState() },
            { MonsterState.Attack, new SlimeAttackState() },
            { MonsterState.Die, new SlimeDieState() }
        };

        foreach (var state in fsmStates.Values)
        {
            //state.Init(this);
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

   
}
