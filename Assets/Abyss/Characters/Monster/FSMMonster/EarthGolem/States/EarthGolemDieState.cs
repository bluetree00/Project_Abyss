using UnityEngine;

public class EarthGolemDieState : IMonsterState
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
    }

    public MonsterController.MonsterState StateUpdate()
    {
        return MonsterController.MonsterState.Die;
    }

    public void Exit()
    {
    }
}
