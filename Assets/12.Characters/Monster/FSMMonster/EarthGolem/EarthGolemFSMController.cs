using UnityEngine;
using Cysharp.Threading.Tasks;
using System.Collections.Generic;
using Unity.Collections;

public class EarthGolemFSMController : MonsterController, IMonsterStateChanger
{
    private Dictionary<MonsterState, IMonsterState> fsmStates = new();
    private MonsterState currentStateKey;
    private IMonsterState currentState;

    [SerializeField, ReadOnly]
    private MonsterState debugCurrentState;

    public IMonsterState CurrentState => currentState;
    public override Define.MonsterType Type => Define.MonsterType.Bat; //CHECKLIST: 테스트 용 Bat 데이터 받아오기 => 추후 수정
    protected override int MonsterId => Define.GetMonsterId(Type);

    protected override async UniTask InitAsync()
    {
        await base.InitAsync();
        InitializeFSM();
        RequestStateChange(MonsterState.Idle);
    }

    private void InitializeFSM()
    {
        RegisterState(MonsterState.Idle, new EarthGolemIdleState());
        RegisterState(MonsterState.Patrol, new EarthGolemPatrolState());
        RegisterState(MonsterState.Chase, new EarthGolemChaseState());
        RegisterState(MonsterState.AttackReady, new EarthGolemAttackReadyState());
        RegisterState(MonsterState.Attack, new EarthGolemAttackState());
        RegisterState(MonsterState.Die, new EarthGolemDieState());
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
