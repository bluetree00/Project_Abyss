/// <summary>
/// IMonsterState 공통 베이스 클래스.
///
/// 기존 각 State 클래스마다 반복되던 아래 boilerplate를 제거한다.
///   - private MonsterController controller;
///   - private IMonsterStateChanger stateChanger;
///   - public void Init(...) { this.controller = ...; this.stateChanger = ...; }
///
/// 상속 클래스는 아래만 override하면 된다.
///   - OnInit()   : ability 캐싱 등 추가 초기화 (선택)
///   - OnEnter()  : 상태 진입 처리 (선택)
///   - OnUpdate() : 매 프레임 로직 + 다음 상태 반환 (필수)
///   - OnExit()   : 상태 종료 처리 (선택)
/// </summary>
public abstract class LeeMonsterStateBase : IMonsterState
{
    protected MonsterController Controller      { get; private set; }
    protected IMonsterStateChanger StateChanger  { get; private set; }
    /// <summary>Controller를 LeeMonsterFSMBase로 캐스팅. 애니메이션 State 이름 접근에 사용.</summary>
    protected LeeMonsterFSMBase LeeFSM           { get; private set; }

    // ── IMonsterState 구현 (봉인) ─────────────────────────────────────

    public void Init(MonsterController controller, IMonsterStateChanger stateChanger)
    {
        Controller    = controller;
        StateChanger  = stateChanger;
        LeeFSM        = controller as LeeMonsterFSMBase;
        OnInit();
    }

    public void Enter() => OnEnter();
    public void Exit()  => OnExit();

    public MonsterController.MonsterState StateUpdate() => OnUpdate();

    // ── 상속 클래스가 override할 훅 ──────────────────────────────────

    /// <summary>Init 시 추가 초기화. ability 캐싱 등에 사용.</summary>
    protected virtual void OnInit()   { }

    /// <summary>상태 진입 시 처리. 애니메이션 재생, 초기값 설정 등.</summary>
    protected virtual void OnEnter()  { }

    /// <summary>상태 종료 시 처리. 이동 정지, 정리 작업 등.</summary>
    protected virtual void OnExit()   { }

    /// <summary>매 프레임 AI 로직. 상태 전환은 StateChanger.RequestStateChange로 처리한다.</summary>
    protected abstract MonsterController.MonsterState OnUpdate();
}
