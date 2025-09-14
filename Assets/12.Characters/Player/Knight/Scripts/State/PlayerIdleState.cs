using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Game.Inputs;

public class PlayerIdleState : IPlayerState
{
    private PlayerController controller;
    private IPlayerStateChanger stateChanger;

    public void Init(PlayerController controller, IPlayerStateChanger stateChanger)
    {
        this.controller = controller;
        this.stateChanger = stateChanger;
    }

    public void Enter()
    {
        controller.Anim.CrossFade("MoveBlend", 0.1f); // 이동 애니메이션 시작
    }

    public void Exit()
    {
   
    }



    public PlayerController.PlayerState Update()
    {
        controller.MoveAbility?.Move(controller, controller.MoveDirection);

        if (controller.MoveDirection.magnitude > 0.01f)
        {
            return PlayerController.PlayerState.Move;
        }

                // 3) 입력 버퍼 소비(우선순위 예시: 회피 > 스킬 > 헤비 > 라이트)
        //    상황에 맞게 커스터마이즈 가능
        if (controller.InputBuffer.TryConsume(Command.Dodge))
            return PlayerController.PlayerState.Dodge;

        if (controller.InputBuffer.TryConsume(Command.Skill))
            return PlayerController.PlayerState.AttackReady; // 스킬 전용 상태가 있으면 그걸로

        if (controller.InputBuffer.TryConsume(Command.Heavy))
            return PlayerController.PlayerState.AttackReady; // 혹은 HeavyChargeStart 등

        if (controller.InputBuffer.TryConsume(Command.Light))
            return PlayerController.PlayerState.AttackReady; // 라이트 시작(콤보 준비)

        return PlayerController.PlayerState.Idle;

    }
}
