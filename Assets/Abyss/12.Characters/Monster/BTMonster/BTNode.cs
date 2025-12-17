using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public abstract class BTNode
{
    public enum Result
    {
        Success,
        Failure,
        Running
    }

    public abstract Result Evaluate();
}
