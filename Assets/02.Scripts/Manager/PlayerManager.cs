using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class PlayerManager
{
    public Transform PlayerTransform { get; private set; }

    public void RegisterPlayer(Transform player)
    {
        PlayerTransform = player;
    }

    public void Clear()
    {
        PlayerTransform = null;
    }
}
