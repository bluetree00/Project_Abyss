using UnityEngine;

public class SkeletonKnightAttackReadyState : IMonsterState
{
    private MonsterController controller;
    private IMonsterStateChanger stateChanger;
    private AttackAbilitySet attackAbilitySet;

    private Define.AttackStyle selectedStyle = Define.AttackStyle.Melee;
    private Define.AttackPurpose selectedPurpose = Define.AttackPurpose.Normal01; // 현재 목적 캐시

    public void Init(MonsterController controller, IMonsterStateChanger stateChanger)
    {
        this.controller = controller;
        this.stateChanger = stateChanger;

        if (!controller.AbilitySet.TryGetAbility<AttackAbilitySet>(Define.MonsterAbilityType.Attack, out attackAbilitySet))
        {
            Debug.LogError("AttackAbilitySet이 할당되어 있지 않습니다!");
        }
    }

    public void Enter()
    {
        controller.StopMoving();

        if (attackAbilitySet == null)
        {
            stateChanger.RequestStateChange(MonsterController.MonsterState.Chase);
            return;
        }

        // 목적 동기화
        if (selectedPurpose != controller.currentAttackPurpose)
            selectedPurpose = controller.currentAttackPurpose;

        // 어빌리티 선택 및 애니메이션 오버라이드 준비 (슬롯 오버라이드는 AttackState로 이동 예정이지만 초기 프리로드 유지 가능)
        var selectedAttack = attackAbilitySet?.SelectAttackAbility(selectedStyle, selectedPurpose);

        if (selectedAttack != null)
        {
            controller.SetCurrentAttackAbility(selectedAttack);

            if (selectedAttack is IAnimClipProvider clipProvider) //어빌리티에 있는 공격 애니메이션을 적용
            {
                var clip = clipProvider.GetAttackAnimationClip();
                string slotKey = Util.GetAnimatorSlotKeyByPurpose(selectedPurpose);
                
                controller.OverrideAnimationClip(slotKey, clip);  // 목적에 따른 안전한 슬롯 키로 교체

            }
        }
    }

    public MonsterController.MonsterState StateUpdate()
    {
        if (controller.playerTarget == null || controller.MyStat == null)
            return MonsterController.MonsterState.Idle;

        float distanceToTarget = Vector3.Distance(controller.transform.position, controller.playerTarget.position);
        bool isInRange = distanceToTarget <= controller.MyStat.attack_range;
        controller.SetInAttackRange(isInRange);

        if (!isInRange)
        {
            controller.currentAttackPurpose = Util.CastToolPurposePlus(selectedPurpose, false);
            stateChanger.RequestStateChange(MonsterController.MonsterState.Chase);
            return MonsterController.MonsterState.Chase;
        }

        if (controller.AttackReadyTime <= 0f)
        {
            stateChanger.RequestStateChange(MonsterController.MonsterState.Attack);
            return MonsterController.MonsterState.Attack;
        }

        Vector3 direction = controller.playerTarget.position - controller.transform.position;
        direction.y = 0f;
        if (direction != Vector3.zero)
        {
            Quaternion targetRotation = Quaternion.LookRotation(direction);
            controller.transform.rotation = Quaternion.Slerp(controller.transform.rotation, targetRotation, Time.deltaTime * 10f);
        }

        return MonsterController.MonsterState.AttackReady;
    }

    public void Exit() { }
}
