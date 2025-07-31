using System;
using UnityEngine;

public class BatIdleState : IMonsterState
{
    private MonsterController controller;
    private IMonsterStateChanger stateChanger;

    private float idleDuration = 3f; // Idle 유지 시간
    private float elapsedTime;

    private IMonsterAbility detectAbility;

    public void Init(MonsterController controller, IMonsterStateChanger stateChanger)
    {
        this.controller = controller;
        this.stateChanger = stateChanger;

        // 탐지 어빌리티 가져오기
        var abilitySet = controller.AbilitySet; 
        detectAbility = abilitySet.GetAbility<IMonsterAbility>(Define.MonsterAbilityType.Detect);
    }

    public void Enter()
    {
        elapsedTime = 0f;

        // Idle 애니메이션 재생 (Blend Tree)
        controller.animator.CrossFade("MoveBlend", 0.1f);
    }

    public void Exit()
    {
        // 필요 시 정리 작업
    }

    public MonsterController.MonsterState Update()
    {
        elapsedTime += Time.deltaTime;

        // 탐지 어빌리티 실행 (플레이어 발견 시 Chase 상태로 전환)
        detectAbility?.Execute();

        if (controller.HasDetectedTarget) // 탐지 성공하면 상태 변경
        {
            stateChanger.RequestStateChange(MonsterController.MonsterState.Chase);
            return MonsterController.MonsterState.Chase;
        }

        // 일정 시간이 지나면 Patrol 상태로 전환
        if (elapsedTime >= idleDuration)
        {
            stateChanger.RequestStateChange(MonsterController.MonsterState.Patrol);
            return MonsterController.MonsterState.Patrol;
        }

        return MonsterController.MonsterState.Idle;
    }
}
