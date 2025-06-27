public class BatAttackState : IMonsterState
{
    private MonsterController controller;
    private IMonsterStateChanger stateChanger;
    private IMonsterAbility attackAbility;
    private BatAnimationEventReceiver animationEventReceiver;

    public void Init(MonsterController controller, IMonsterStateChanger stateChanger)
    {
        this.controller = controller;
        this.stateChanger = stateChanger;
        attackAbility = controller.AbilitySet.GetAbility<IMonsterAbility>(Define.MonsterAbilityType.Attack);

        animationEventReceiver = controller.GetComponent<BatAnimationEventReceiver>();
    }

    public void Enter()
    {
        controller.SetAttack(true); // 공격 시작
        attackAbility.Execute();    // 공격 어빌리티 실행
        controller.Anim.CrossFade("NormalAttack_1", 0.1f);
    }

    public MonsterController.MonsterState Update()
    {
        // 공격이 끝났다면 추적 상태로 전환
        if (!controller.IsAttacking)
        {
            stateChanger.RequestStateChange(MonsterController.MonsterState.Chase);
            return MonsterController.MonsterState.Chase;
        }

        return MonsterController.MonsterState.Attack;
    }

    public void Exit()
    {
        controller.StopMoving();
    }
}
