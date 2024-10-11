using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(menuName = "Quest/Task/Target/GameObject", fileName = "TargetObj_")]
public class GameObjectTarget : TaskTarget
{
    [SerializeField]
    private GameObject value;

    public override object Value => value;

    public override bool IsEqual(object target)
    {
        var targetObj = target as GameObject;
        if (targetObj == null)
            return false;
        return targetObj.name.Contains(value.name);
    }
}
