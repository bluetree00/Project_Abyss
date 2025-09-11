public class BatAttackState : IMonsterState
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

        // 미리 준비된 공격 어빌리티 실행
        controller.CurrentAttackAbility?.Execute();
    }

    public MonsterController.MonsterState Update()
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
