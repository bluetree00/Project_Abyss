using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class PlayerIdleState : IPlayerState
{
    private PlayerController controller;
    private IPlayerStateChanger stateChanger;

    public void Enter()
    {
        controller.Anim.CrossFade("MoveBlend", 0.2f); // 이동 애니메이션 시작
    }

    public void Exit()
    {
        throw new System.NotImplementedException();
    }

    public void Init(PlayerController controller, PlayerController stateChanger)
    {
        throw new System.NotImplementedException();
    }

    public PlayerController.PlayerState Update()
    {
        throw new System.NotImplementedException();
    }
}
