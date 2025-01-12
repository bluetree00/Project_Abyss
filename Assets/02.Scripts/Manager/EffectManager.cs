using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class EffectManager
{
    //FIXME:임시방편으로 매니저에 매개변수 넘기는 함수
    public void SetEffectPooler(string weaponName)
    {
        Managers.Instance.CreateNewObjectPooler(weaponName);
    } 
}
