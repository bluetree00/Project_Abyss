using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public interface IPlayerState
{
    void Init(PlayerController controller, PlayerController stateChanger);
    void Enter();
    PlayerController.PlayerState Update();
    void Exit();
}