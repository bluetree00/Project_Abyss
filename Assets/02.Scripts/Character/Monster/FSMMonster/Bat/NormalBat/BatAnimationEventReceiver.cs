using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class BatAnimationEventReceiver : MonoBehaviour
{
    private MonsterController controller;
    private string effectName;

    public void Init(MonsterController ctrl, string effectName)
    {
        this.controller = ctrl;
        this.effectName = effectName;
    }

    public void OnAttackStart()
    {
        if (!string.IsNullOrEmpty(effectName))
        {
            //Managers.EffectManager.Play(effectName, transform.position);
        }

     //   controller?.CurrentState?.OnAttackHit(); // 상태에도 알림
    }
}
