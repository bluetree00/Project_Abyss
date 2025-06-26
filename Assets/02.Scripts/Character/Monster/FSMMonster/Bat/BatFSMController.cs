using UnityEngine;
using UnityEngine.AI;
using System.Collections.Generic;
using System.Threading.Tasks;
using Unity.Collections;

public class BatFSMController : MonsterController, IMonsterStateChanger
{
    // 상태 키와 상태 인스턴스를 매핑하는 딕셔너리
    private Dictionary<MonsterState, IMonsterState> fsmStates = new();

    // 현재 상태의 키(enum)
    private MonsterState currentStateKey;

    // 현재 활성화된 상태 인스턴스
    private IMonsterState currentState;

    [SerializeField, ReadOnly]
    private MonsterState debugCurrentState;

    public override Define.MonsterType Type => Define.MonsterType.Bat;

    // 비동기 초기화 메서드, 부모 초기화 후 FSM 초기화 및 초기 상태 지정
    protected override async Task InitAsync()
    {
        await base.InitAsync();    // 부모 클래스 초기화 수행
        InitializeFSM();           // FSM 상태들을 등록하고 초기화
        RequestStateChange(MonsterState.Idle); // 초기 상태를 Idle로 설정

    }

    

    // FSM 상태별 인스턴스를 생성 및 등록하는 메서드
    private void InitializeFSM()
    {
        RegisterState(MonsterState.Idle, new BatIdleState());
        RegisterState(MonsterState.Patrol, new BatPatrolState());
        RegisterState(MonsterState.Chase, new BatChaseState());
        RegisterState(MonsterState.Attack, new BatAttackState());
        RegisterState(MonsterState.Die, new BatDieState());
    }

    // 특정 상태를 딕셔너리에 등록하고 상태 초기화 진행
    private void RegisterState(MonsterState key, IMonsterState state)
    {
        // 상태에 이 컨트롤러와 상태 변경 요청 인터페이스를 전달하여 초기화
        state.Init(this, this);

        // 딕셔너리에 상태 등록
        fsmStates[key] = state;
    }

    // 상태 변경 요청 처리 메서드, 같은 상태 요청 시 무시
    public void RequestStateChange(MonsterState newState)
    {
        if (currentStateKey == newState)
            return; // 이미 같은 상태면 변경하지 않음

        currentState?.Exit();    // 현재 상태가 있으면 종료 처리
        currentStateKey = newState;   // 상태 키 변경
        debugCurrentState = newState; // <- 인스펙터용 상태 업데이트
        currentState = fsmStates[newState]; // 새로운 상태 할당
        currentState.Enter();    // 새로운 상태 진입 처리
    }

    // 매 프레임 호출되며 현재 상태의 Update 로직 실행
    public override void HandleAI()
    {
        currentState?.Update();
    }


}
