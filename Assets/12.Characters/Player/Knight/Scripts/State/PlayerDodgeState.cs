using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class PlayerDodgeState : IPlayerState
{
    public void Enter()
    {
        Debug.Log("d");
    }

    public void Exit()
    {
     
    }

    public void Init(PlayerController controller, IPlayerStateChanger stateChanger)
    {

    }

    PlayerController.PlayerState IPlayerState.Update()
    {
        return PlayerController.PlayerState.Dodge;
    }
}
