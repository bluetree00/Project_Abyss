using UnityEngine;

public class SlimeAttackState : IMonsterState
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
        controller.SetAttack(true);
        controller.CurrentAttackAbility?.Execute();
    }

    public MonsterController.MonsterState StateUpdate()
    {
        if (!controller.IsAttacking)
        {
            stateChanger.RequestStateChange(MonsterController.MonsterState.Chase);
            return MonsterController.MonsterState.Chase;
        }
        return MonsterController.MonsterState.Attack;
    }

    public void Exit()
    {
        // controller.StopMoving();
    }
}
