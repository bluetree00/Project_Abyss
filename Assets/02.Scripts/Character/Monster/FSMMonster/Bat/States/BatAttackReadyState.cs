using UnityEngine;

public class BatAttackReadyState : IMonsterState
{
    private MonsterController controller;
    private IMonsterStateChanger stateChanger;
    private AttackAbilitySet attackAbilitySet;

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
        // ✅ 현재 거리 측정하여 IsInAttackRange 갱신
        float distanceToTarget = Vector3.Distance(controller.transform.position, controller.playerTarget.position);
        bool isInRange = distanceToTarget <= controller.MyStat.attack_range;

        controller.SetInAttackRange(isInRange); // 수동 갱신

        // ✅ 공격 범위 이탈 시 추적 상태로 전환
        if (!isInRange)
        {
            stateChanger.RequestStateChange(MonsterController.MonsterState.Chase);
            return MonsterController.MonsterState.Chase;
        }

        // ✅ 쿨타임이 끝나면 공격 상태로 전환
        if (controller.AttackReadyTime <= 0f)
        {
            stateChanger.RequestStateChange(MonsterController.MonsterState.Attack);
            return MonsterController.MonsterState.Attack;
        }

        // ✅ 공격 방향 회전 보정
        Vector3 direction = controller.playerTarget.position - controller.transform.position;
        direction.y = 0f;
        if (direction != Vector3.zero)
        {
            Quaternion targetRotation = Quaternion.LookRotation(direction);
            controller.transform.rotation = Quaternion.Slerp(controller.transform.rotation, targetRotation, Time.deltaTime * 10f);
        }

        return MonsterController.MonsterState.AttackReady;
    }



    public void Exit()
    {
        // 필요시 처리
    }
}
