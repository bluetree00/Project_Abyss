using UnityEngine;

public class SlimeDieState : IMonsterState
{
    private MonsterController controller;
    private IMonsterStateChanger stateChanger;

    public void Init(MonsterController controller, IMonsterStateChanger stateChanger)
    {
        this.controller = controller;
        this.stateChanger = stateChanger;
    }

    public void Enter()
    {
        // 사망 진입 처리
    }

    public MonsterController.MonsterState StateUpdate()
    {
        // 종결 상태 유지
        return MonsterController.MonsterState.Die;
    }

    public void Exit()
    {
        // 정리 작업
    }
}
