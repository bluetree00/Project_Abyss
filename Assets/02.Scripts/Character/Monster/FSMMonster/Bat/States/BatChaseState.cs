using UnityEngine;

public class BatChaseState : IMonsterState
{
    private MonsterController controller;
    private IMonsterStateChanger stateChanger;
    private IMonsterAbility chaseAbility;

    public void Init(MonsterController controller, IMonsterStateChanger stateChanger)
    {
        this.controller = controller;
        this.stateChanger = stateChanger;

        chaseAbility = controller.AbilitySet.GetAbility<IMonsterAbility>(Define.MonsterAbilityType.Chase);
    }

    public void Enter()
    {
        controller.Anim.CrossFade("MoveBlend", 1f);
    }

    public void Exit()
    {
        controller.StopMoving();
    }

    public MonsterController.MonsterState Update()
    {
        chaseAbility?.Execute();

        if (controller.IsInAttackRange)
        {
            stateChanger.RequestStateChange(MonsterController.MonsterState.Attack);
            return MonsterController.MonsterState.Attack;
        }

        return MonsterController.MonsterState.Chase;
    }
}
