using UnityEngine;
using System.Collections.Generic;
using Unity.Collections;
using Cysharp.Threading.Tasks;

public class SnailFSMController : MonsterController, IMonsterStateChanger
{
    private Dictionary<MonsterState, IMonsterState> fsmStates = new();
    private MonsterState currentStateKey;
    private IMonsterState currentState;

    [SerializeField, ReadOnly]
    private MonsterState debugCurrentState;

    public IMonsterState CurrentState => currentState;
    //CHECKLIST: 테스트 용 Bat 데이터 받아오기 => 추후 수정
    public override Define.MonsterType Type => Define.MonsterType.Bat;
    protected override int MonsterId => Define.GetMonsterId(Type);

    protected override async UniTask InitAsync()
    {
        await base.InitAsync();
        InitializeFSM();
        RequestStateChange(MonsterState.Idle);
    }

    private void InitializeFSM()
    {
        RegisterState(MonsterState.Idle, new SnailIdleState());
        RegisterState(MonsterState.Patrol, new SnailPatrolState());
        RegisterState(MonsterState.Chase, new SnailChaseState());
        RegisterState(MonsterState.AttackReady, new SnailAttackReadyState());
        RegisterState(MonsterState.Attack, new SnailAttackState());
        RegisterState(MonsterState.Die, new SnailDieState());
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
        debugCurrentState = newState;
        currentState = fsmStates[newState];
        currentState.Enter();
    }

    public override void HandleAI()
    {
        currentState?.StateUpdate();
    }
}
