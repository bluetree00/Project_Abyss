using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System;

public class ConditionNode : BTNode
{
    private Func<bool> condition;

    public ConditionNode(Func<bool> condition)
    {
        this.condition = condition;
    }

    public override Result Evaluate()
    {
        return condition.Invoke() ? Result.Success : Result.Failure;
    }
}
