using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class GameEventManager
{
    #region 스테이지 이벤트
    public event Action<int> portal;    // 스테이지 포탈 이벤트

    public void TriggerPortal(int steps)
    {
        portal?.Invoke(steps);
    }
    #endregion
}
