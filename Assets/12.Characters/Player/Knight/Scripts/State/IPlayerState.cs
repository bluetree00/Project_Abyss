using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public interface IPlayerState
{
    void Init(PlayerController controller, IPlayerStateChanger stateChanger);
    void Enter();
    PlayerController.PlayerState Update();
    void Exit();
}