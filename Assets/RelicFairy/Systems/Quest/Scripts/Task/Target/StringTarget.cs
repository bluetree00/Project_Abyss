using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(menuName = "Quest/Task/Target/String", fileName = "Target_")]
public class StringTarget : TaskTarget
{
    [SerializeField]
    private string value;

    public override object Value => value;

    public override bool IsEqual(object target)
    {
        // value == "*" 이면 카테고리 내 모든 보고를 수용 (예: 몬스터 아무거나 N마리, 골드 누적)
        if (value == "*")
            return true;

        string targetAsString = target as string;
        if (targetAsString == null)
            return false;
        return value == targetAsString;
    }
}
