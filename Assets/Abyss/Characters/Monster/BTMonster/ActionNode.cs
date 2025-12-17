public class ActionNode : BTNode
{
    public delegate Result ActionNodeDelegate();
    private ActionNodeDelegate action;

    public ActionNode(ActionNodeDelegate action)
    {
        this.action = action;
    }

    public override Result Evaluate()
    {
        return action?.Invoke() ?? Result.Failure;
    }
}
