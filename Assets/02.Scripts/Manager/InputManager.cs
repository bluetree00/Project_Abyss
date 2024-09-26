using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class InputManager
{
    public Action KeyAction = null;

    public void OnUpdate()
    {
        // 키 입력 상태가 있을 때만 KeyAction을 호출
        if (KeyAction != null)
        {
            KeyAction.Invoke();
        }
    }

    public void Clear()
    {
        KeyAction = null;
    }
}
