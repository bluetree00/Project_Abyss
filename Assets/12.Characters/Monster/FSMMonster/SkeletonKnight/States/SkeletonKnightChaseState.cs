using UnityEngine;

public class SkeletonKnightChaseState : IMonsterState
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
        controller.animator.CrossFade("MoveBlend", 0.5f);
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

    public void Exit()
    {
        controller.StopMoving();
    }
}
