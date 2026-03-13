using UnityEngine;

/// <summary>
/// 공통 Patrol 상태.
/// - PatrolAbility로 순찰 이동
/// - DetectAbility로 감지 → Chase 전환
/// </summary>
public class LeePatrolState : LeeMonsterStateBase
{
    private IMonsterAbility _patrolAbility;
    private IMonsterAbility _detectAbility;

    protected override void OnInit()
    {
        _patrolAbility = Controller.AbilitySet.GetAbility<IMonsterAbility>(Define.MonsterAbilityType.Patrol);
        _detectAbility = Controller.AbilitySet.GetAbility<IMonsterAbility>(Define.MonsterAbilityType.Detect);
    }

    protected override void OnEnter()
    {
        Controller.animator.CrossFade(LeeFSM?.AnimMove ?? "MoveBlend", 0.1f);
    }

    protected override void OnExit()
    {
        Controller.StopMoving();
    }

    protected override MonsterController.MonsterState OnUpdate()
    {
        _patrolAbility?.Execute();
        _detectAbility?.Execute();

        if (Controller.HasDetectedTarget)
        {
            StateChanger.RequestStateChange(MonsterController.MonsterState.Chase);
            return MonsterController.MonsterState.Chase;
        }

        return MonsterController.MonsterState.Patrol;
    }
}
