using UnityEngine;

/// <summary>
/// 귀환 상태 (IReturnableMonster 구현 몬스터 공용 / MonsterState.Patrol 슬롯 사용).
/// 스폰 지점으로 이동하고, 도착하면 Idle로 전환한다.
/// </summary>
public class LeeReturnState : LeeMonsterStateBase
{
    private IReturnableMonster _returnable;

    protected override void OnInit()
    {
        _returnable = Controller as IReturnableMonster;
    }

    protected override void OnEnter()
    {
        Controller.SetDetected(false);
        Controller.SetInAttackRange(false);

        if (Controller.agent != null && Controller.MyStat != null)
            Controller.agent.speed = Controller.MyStat.move_speed;

        if (_returnable != null)
            Controller.MoveTo(_returnable.SpawnPoint);

        Controller.animator.CrossFade(LeeFSM?.AnimMove ?? "MoveBlend", 0.1f);
    }

    protected override void OnExit()
    {
        Controller.StopMoving();
    }

    protected override MonsterController.MonsterState OnUpdate()
    {
        if (_returnable == null)
            return MonsterController.MonsterState.Patrol;

        float dist = Vector3.Distance(Controller.transform.position, _returnable.SpawnPoint);
        if (dist <= _returnable.ArrivalDistance)
        {
            StateChanger.RequestStateChange(MonsterController.MonsterState.Idle);
            return MonsterController.MonsterState.Idle;
        }

        return MonsterController.MonsterState.Patrol;
    }
}
