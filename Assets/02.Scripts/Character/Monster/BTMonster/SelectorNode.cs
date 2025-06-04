using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class SelectorNode : BTNode
{
    private List<BTNode> children = new List<BTNode>();

    public SelectorNode(params BTNode[] nodes)
    {
        children.AddRange(nodes);
    }

    public override Result Evaluate()
    {
        foreach (var child in children)
        {
            var result = child.Evaluate();
            if (result == Result.Success)
                return Result.Success;
        }
        return Result.Failure;
    }
}
