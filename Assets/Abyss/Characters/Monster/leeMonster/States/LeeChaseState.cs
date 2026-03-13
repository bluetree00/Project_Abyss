using UnityEngine;

/// <summary>
/// 공통 Chase 상태.
/// - ChaseAbility로 플레이어 추적
/// - 공격 범위 진입 → AttackReady 전환
/// - [기존 개선] 감지 범위 이탈 → Patrol 복귀 (기존 SlimeChaseState에는 없던 처리)
/// </summary>
public class LeeChaseState : LeeMonsterStateBase
{
    private IMonsterAbility _chaseAbility;
    private IMonsterAbility _detectAbility;

    protected override void OnInit()
    {
        _chaseAbility  = Controller.AbilitySet.GetAbility<IMonsterAbility>(Define.MonsterAbilityType.Chase);
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
        _chaseAbility?.Execute();
        _detectAbility?.Execute();

        // 공격 범위 진입 → AttackReady
        if (Controller.IsInAttackRange)
        {
            StateChanger.RequestStateChange(MonsterController.MonsterState.AttackReady);
            return MonsterController.MonsterState.AttackReady;
        }

        // 타깃 상실 → Patrol 복귀 (기존 SlimeChaseState에는 없던 처리)
        if (!Controller.HasDetectedTarget)
        {
            StateChanger.RequestStateChange(MonsterController.MonsterState.Patrol);
            return MonsterController.MonsterState.Patrol;
        }

        return MonsterController.MonsterState.Chase;
    }
}
