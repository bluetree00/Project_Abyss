using UnityEngine;
using UnityEngine.AI;
using System.Collections.Generic;
using System.Threading.Tasks;
using Unity.Collections;
using Cysharp.Threading.Tasks;

public class BatFSMController : MonsterController, IMonsterStateChanger
{

    
    // FSM 상태 키(enum)와 상태 인스턴스를 매핑하는 딕셔너리
    private Dictionary<MonsterState, IMonsterState> fsmStates = new();

    // 현재 상태의 키
    private MonsterState currentStateKey;

    // 현재 활성화된 상태 인스턴스
    private IMonsterState currentState;

    // 디버그용 현재 상태 노출
    [SerializeField, ReadOnly]
    private MonsterState debugCurrentState;

    // 외부에서 상태 접근 허용 (읽기 전용)
    public IMonsterState CurrentState => currentState;

    // 이 몬스터의 타입 정보 (부모에 정의된 추상 프로퍼티 구현)
    public override Define.MonsterType Type => Define.MonsterType.Bat;

    // 몬스터 ID를 정의된 타입에 따라 반환하는 프로퍼티
    // Define 클래스의 GetMonsterId 메서드를 사용하여 몬스터 ID를 가져옴
    protected override int MonsterId => Define.GetMonsterId(Type);


    // 비동기 초기화 메서드, 부모 초기화 후 FSM 초기화 및 초기 상태 지정
    protected override async UniTask InitAsync()
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
        RegisterState(MonsterState.AttackReady, new BatAttackReadyState());
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
