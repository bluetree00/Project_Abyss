using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class PlayerManager
{
    public event Action<Transform> OnPlayerSpawned;
    public Transform PlayerTransform { get; private set; }

    public void SetPlayer(Transform player)
    {
        PlayerTransform = player;
        OnPlayerSpawned?.Invoke(player);
    }
}
