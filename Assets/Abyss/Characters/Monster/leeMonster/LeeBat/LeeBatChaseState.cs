using UnityEngine;

/// <summary>
/// 귀환 기능이 있는 Chase 상태 (IReturnableMonster 구현 몬스터 공용).
/// 스폰 거리 초과 시 귀환(Patrol)으로 전환하는 로직 포함.
/// </summary>
public class LeeReturnChaseState : LeeMonsterStateBase
{
    private IMonsterAbility    _chaseAbility;
    private IReturnableMonster _returnable;

    protected override void OnInit()
    {
        _chaseAbility = Controller.AbilitySet.GetAbility<IMonsterAbility>(Define.MonsterAbilityType.Chase);
        _returnable   = Controller as IReturnableMonster;
    }

    protected override void OnEnter()
    {
        if (Controller.agent != null && Controller.MyStat != null)
            Controller.agent.speed = Controller.MyStat.move_speed;

        Controller.animator.CrossFade(LeeFSM?.AnimMove ?? "MoveBlend", 0.1f);
    }

    protected override void OnExit()
    {
        Controller.StopMoving();
    }

    protected override MonsterController.MonsterState OnUpdate()
    {
        _chaseAbility?.Execute();

        if (Controller.IsInAttackRange)
        {
            StateChanger.RequestStateChange(MonsterController.MonsterState.AttackReady);
            return MonsterController.MonsterState.AttackReady;
        }

        if (_returnable != null)
        {
            float distFromSpawn = Vector3.Distance(Controller.transform.position, _returnable.SpawnPoint);
            if (distFromSpawn > _returnable.ReturnDistance)
            {
                StateChanger.RequestStateChange(MonsterController.MonsterState.Patrol);
                return MonsterController.MonsterState.Patrol;
            }
        }

        return MonsterController.MonsterState.Chase;
    }
}
