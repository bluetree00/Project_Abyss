using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class PlayerAttackState : IPlayerState
{
    public void Enter()
    {
        Debug.Log("a");
    }

    public void Exit()
    {
        
    }

    public void Init(PlayerController controller, IPlayerStateChanger stateChanger)
    {
        
    }

    public PlayerController.PlayerState Update()
    {
             return PlayerController.PlayerState.Attack;
    }
}
