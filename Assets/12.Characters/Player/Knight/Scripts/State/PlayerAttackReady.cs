using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class PlayerAttackReady : IPlayerState
{
    public void Enter()
    {
        Debug.Log("ar");
    }

    public void Exit()
    {
    
    }

    public void Init(PlayerController controller, IPlayerStateChanger stateChanger)
    {
    
    }

    public PlayerController.PlayerState Update()
    {
        return PlayerController.PlayerState.AttackReady;
    }
}
