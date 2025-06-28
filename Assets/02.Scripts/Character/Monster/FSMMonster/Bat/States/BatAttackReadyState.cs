using UnityEngine;

public class BatAttackReadyState : IMonsterState
{
    private MonsterController controller;
    private IMonsterStateChanger stateChanger;
    private IMonsterAbility attackAbility;
    private float readyTime = 0.5f; // 공격 준비 시간
    private float timer;

    public void Init(MonsterController controller, IMonsterStateChanger stateChanger)
    {
        this.controller = controller;
        this.stateChanger = stateChanger;
        attackAbility = controller.AbilitySet.GetAbility<IMonsterAbility>(Define.MonsterAbilityType.Attack);
    }

    public void Enter()
    {
        controller.StopMoving(); // 공격 준비 중엔 멈춤
        timer = readyTime;

        // 공격 전 애니메이션이나 이펙트 시작
        controller.Anim.CrossFade("AttackReady", 0.1f);
        // 필요시 공격 타입 결정 로직 삽입 가능
        // e.g., attackAbility = DecideNextAttack();
    }

    public MonsterController.MonsterState Update()
    {
        timer -= Time.deltaTime;

        if (timer <= 0f)
        {
            stateChanger.RequestStateChange(MonsterController.MonsterState.Attack);
            return MonsterController.MonsterState.Attack;
        }

        return MonsterController.MonsterState.AttackReady;
    }

    public void Exit()
    {
        // 아무것도 안 해도 됨 (혹은 이펙트 정리 등)
    }
}
