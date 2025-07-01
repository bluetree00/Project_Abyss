using UnityEngine;

public class BatAttackReadyState : IMonsterState
{
    private MonsterController controller;
    private IMonsterStateChanger stateChanger;
    private AttackAbilitySet attackAbilitySet; // AttackAbilitySet으로 캐스팅
    private float readyTime = 0.5f; // 공격 준비 시간
    private float timer;

    // 예: 준비 단계에서 사용할 스타일 지정 (외부에서 세팅 가능)
    private Define.AttackStyle selectedStyle = Define.AttackStyle.Melee; 
    private Define.AttackPurpose selectedPurpose = Define.AttackPurpose.Normal;

    public void Init(MonsterController controller, IMonsterStateChanger stateChanger)
    {
        this.controller = controller;
        this.stateChanger = stateChanger;

        // AbilitySet에서 AttackAbilitySet 가져오기
        var ability = controller.AbilitySet.GetAbility<IMonsterAbility>(Define.MonsterAbilityType.Attack);

        attackAbilitySet = ability as AttackAbilitySet;
        if (attackAbilitySet == null)
            Debug.LogError("AttackAbilitySet이 할당되어 있지 않습니다!");
    }

    public void Enter()
    {
        controller.StopMoving(); // 공격 준비 중엔 멈춤
        timer = readyTime;

        // 공격 준비 애니메이션 재생
        controller.Anim.CrossFade("AttackReady", 0.1f);

        // 필요하면 여기서 스타일, 목적을 결정하는 로직 추가 가능
    }

    public MonsterController.MonsterState Update()
    {
        timer -= Time.deltaTime;

        if (timer <= 0f)
        {
            var selectedAttack = attackAbilitySet?.SelectAttackAbility(selectedStyle, selectedPurpose);
            if (selectedAttack != null)
            {
                controller.SetCurrentAttackAbility(selectedAttack); // 💡 여기에 저장
            }

            stateChanger.RequestStateChange(MonsterController.MonsterState.Attack);
            return MonsterController.MonsterState.Attack;
        }

        return MonsterController.MonsterState.AttackReady;
    }


    public void Exit()
    {
        // 필요 시 이펙트 정리 등 처리
    }
}
