using UnityEngine;
using UnityEngine.AI;
using System.Collections.Generic;
using System.Threading.Tasks;

public class BatFSMController : MonsterController, IMonsterStateChanger
{
    private Dictionary<MonsterState, IMonsterState> fsmStates = new();
    private MonsterState currentStateKey;
    private IMonsterState currentState;

    protected override async Task InitAsync()
    {
        await base.InitAsync();
        InitializeFSM();
        RequestStateChange(MonsterState.Idle);
    }

    private void InitializeFSM()
    {
        RegisterState(MonsterState.Idle, new BatIdleState());
        RegisterState(MonsterState.Patrol, new BatPatrolState());
        RegisterState(MonsterState.Chase, new BatChaseState());
        RegisterState(MonsterState.Attack, new BatAttackState());
        RegisterState(MonsterState.Die, new BatDieState());
    }

    private void RegisterState(MonsterState key, IMonsterState state)
    {
        state.Init(this, this);
        fsmStates[key] = state;
    }

    public void RequestStateChange(MonsterState newState)
    {
        if (currentStateKey == newState)
            return;

        currentState?.Exit();
        currentStateKey = newState;
        currentState = fsmStates[newState];
        currentState.Enter();
    }

    public override void HandleAI()
    {
        currentState?.Update();
    }

}
