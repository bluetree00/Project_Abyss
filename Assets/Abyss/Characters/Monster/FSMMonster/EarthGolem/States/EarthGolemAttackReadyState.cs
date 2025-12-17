using UnityEngine;

public class EarthGolemAttackReadyState : IMonsterState
{
    private MonsterController controller;
    private IMonsterStateChanger stateChanger;
    private AttackAbilitySet attackAbilitySet;

    private Define.AttackStyle selectedStyle = Define.AttackStyle.Melee;
    private Define.AttackPurpose selectedPurpose = Define.AttackPurpose.Normal01;

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

        var selectedAttack = attackAbilitySet.SelectAttackAbility(selectedStyle, selectedPurpose);

        if (selectedAttack != null)
        {
            controller.SetCurrentAttackAbility(selectedAttack);

            if (selectedAttack is IAnimClipProvider clipProvider)
            {
                var clip = clipProvider.GetAttackAnimationClip();
                controller.OverrideAnimationClip("Attack", clip);
            }
        }

        controller.animator.CrossFade("AttackReady", 0.1f);
    }

    public MonsterController.MonsterState StateUpdate()
    {
        if (controller.playerTarget == null || controller.MyStat == null)
            return MonsterController.MonsterState.AttackReady;

        float distanceToTarget = Vector3.Distance(controller.transform.position, controller.playerTarget.position);
        bool isInRange = distanceToTarget <= controller.MyStat.attack_range;
        controller.SetInAttackRange(isInRange);

        if (!isInRange)
        {
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

    public void Exit()
    {
    }
}
