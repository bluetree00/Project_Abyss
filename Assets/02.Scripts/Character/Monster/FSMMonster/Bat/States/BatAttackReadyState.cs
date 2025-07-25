using UnityEngine;

public class BatAttackReadyState : IMonsterState
{
    private MonsterController controller;
    private IMonsterStateChanger stateChanger;
    private AttackAbilitySet attackAbilitySet;

    private float readyTime = 0.5f;
    private float timer;

    private Define.AttackStyle selectedStyle = Define.AttackStyle.Melee; 
    private Define.AttackPurpose selectedPurpose = Define.AttackPurpose.Normal;

    public void Init(MonsterController controller, IMonsterStateChanger stateChanger)
    {
        this.controller = controller;
        this.stateChanger = stateChanger;

        var ability = controller.AbilitySet.GetAbility<IMonsterAbility>(Define.MonsterAbilityType.Attack);
        attackAbilitySet = ability as AttackAbilitySet;

        if (attackAbilitySet == null)
            Debug.LogError("AttackAbilitySet이 할당되어 있지 않습니다!");
    }

    public void Enter()
    {
        controller.StopMoving();
        timer = readyTime;

        // 어빌리티 선택 및 애니메이션 오버라이드 준비
        var selectedAttack = attackAbilitySet?.SelectAttackAbility(selectedStyle, selectedPurpose);

        if (selectedAttack != null)
        {
            controller.SetCurrentAttackAbility(selectedAttack);

            if (selectedAttack is IAnimClipProvider clipProvider) //어빌리티에 있는 공격 애니메이션을 적용
            {
                var clip = clipProvider.GetAttackAnimationClip();
                controller.OverrideAnimationClip("Attack", clip);  // 몬스터 컨트롤러 내 함수 호출

            }
        }

        controller.animator.CrossFade("AttackReady", 0.1f);
    }

    public MonsterController.MonsterState Update()
    {
        timer -= Time.deltaTime;

        if(timer <= 0f)
        {
            // 공격 상태로 전환
            stateChanger.RequestStateChange(MonsterController.MonsterState.Attack);
            return MonsterController.MonsterState.Attack;
        }

        return MonsterController.MonsterState.AttackReady;
    }

    public void Exit()
    {
        // 필요시 처리
    }
}
