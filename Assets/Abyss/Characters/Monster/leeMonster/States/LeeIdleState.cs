using UnityEngine;

/// <summary>
/// 공통 Idle 상태.
/// - idleDuration 동안 대기 후 Patrol로 전환
/// - 그 사이 플레이어 감지 시 Chase로 전환
/// </summary>
public class LeeIdleState : LeeMonsterStateBase
{
    private readonly float _idleDuration;
    private float _elapsed;
    private IMonsterAbility _detectAbility;

    /// <param name="idleDuration">Idle 유지 시간(초). 기본 3초.</param>
    public LeeIdleState(float idleDuration = 3f)
    {
        _idleDuration = idleDuration;
    }

    protected override void OnInit()
    {
        _detectAbility = Controller.AbilitySet.GetAbility<IMonsterAbility>(Define.MonsterAbilityType.Detect);
    }

    protected override void OnEnter()
    {
        _elapsed = 0f;
        Controller.StopMoving();
        Controller.animator.CrossFade(LeeFSM?.AnimIdle ?? "MoveBlend", 0.1f);
    }

    protected override MonsterController.MonsterState OnUpdate()
    {
        _elapsed += Time.deltaTime;
        _detectAbility?.Execute();

        if (Controller.HasDetectedTarget)
        {
            StateChanger.RequestStateChange(MonsterController.MonsterState.Chase);
            return MonsterController.MonsterState.Chase;
        }

        if (_elapsed >= _idleDuration)
        {
            StateChanger.RequestStateChange(MonsterController.MonsterState.Patrol);
            return MonsterController.MonsterState.Patrol;
        }

        return MonsterController.MonsterState.Idle;
    }
}
