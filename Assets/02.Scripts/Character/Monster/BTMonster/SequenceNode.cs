using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class SequenceNode : BTNode
{
    private List<BTNode> children = new List<BTNode>();

    public SequenceNode(params BTNode[] nodes)
    {
        children.AddRange(nodes);
    }

    public override Result Evaluate()
    {
        foreach (var child in children)
        {
            var result = child.Evaluate();
            if (result != Result.Success)
                return result;
        }
        return Result.Success;
    }
}
