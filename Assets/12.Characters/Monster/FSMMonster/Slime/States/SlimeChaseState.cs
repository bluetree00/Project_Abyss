using UnityEngine;

public class SlimeChaseState : IMonsterState
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
        controller.animator.CrossFade("MoveBlend", 1f);
    }

    public void Exit()
    {
        controller.StopMoving();
    }

    public MonsterController.MonsterState StateUpdate()
    {
        chaseAbility?.Execute();

        if (controller.IsInAttackRange)
        {
            stateChanger.RequestStateChange(MonsterController.MonsterState.AttackReady);
            return MonsterController.MonsterState.AttackReady;
        }

        return MonsterController.MonsterState.Chase;
    }
}
