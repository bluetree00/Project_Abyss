using System.Linq;
using UnityEngine;

public enum TaskState
{
    Inactive,
    Running,
    Complete
}

[CreateAssetMenu(menuName = "Quest/Task/Task", fileName = "Task_")]
public class Task : ScriptableObject
{
    #region Events
    public delegate void StateChangedHandler(Task task, TaskState currentState, TaskState prevState);
    public delegate void SuccessChangedHandler(Task task, int currentSuccess, int prevSuccess);
    #endregion

    [SerializeField] private Category category;

    [Header("Text")]
    [SerializeField] private string codeName;
    [SerializeField] private string description;

    [Header("Action")]
    [SerializeField] private TaskAction action;

    [Header("Target")]
    [SerializeField] private TaskTarget[] targets;

    [Header("Setting")]
    [SerializeField] private IntialSuccessValue intialSuccessValue;
    [SerializeField] private int needSuccessToComplete;
    [SerializeField] private bool canReceiveReportDuringCompletion;

    private TaskState _state;
    private int _currentSuccess;

    public event StateChangedHandler onStateChanged;
    public event SuccessChangedHandler onSuccessChanged;

    public int CurrentSuccess
    {
        get => _currentSuccess;
        set
        {
            int prevSuccess = _currentSuccess;
            _currentSuccess = Mathf.Clamp(value, 0, needSuccessToComplete);
            if (_currentSuccess != prevSuccess)
            {
                State = _currentSuccess == needSuccessToComplete ? TaskState.Complete : TaskState.Running;
                onSuccessChanged?.Invoke(this, _currentSuccess, prevSuccess);
            }
        }
    }

    public Category Category => category;
    public int NeedSuccessToComplete => needSuccessToComplete;
    public string CodeName => codeName;
    public string Description => description;

    public TaskState State
    {
        get => _state;
        set
        {
            var prevState = _state;
            _state = value;
            onStateChanged?.Invoke(this, _state, prevState);
        }
    }

    public bool IsComplete => State == TaskState.Complete;

    public Quest Owner { get; private set; }

    public void Setup(Quest owner)
    {
        Owner = owner;
    }

    public void Start()
    {
        State = TaskState.Running;
        if (intialSuccessValue)
            CurrentSuccess = intialSuccessValue.GetValue(this);
    }

    public void End()
    {
        onStateChanged = null;
        onSuccessChanged = null;
    }

    public void ReceiveReport(int successCount)
    {
        CurrentSuccess = action.Run(this, CurrentSuccess, successCount);
    }

    public void Complete()
    {
        CurrentSuccess = needSuccessToComplete;
    }

    public bool IsTarget(string category, object target)
        => Category == category &&
           targets.Any(x => x.IsEqual(target)) &&
           (!IsComplete || canReceiveReportDuringCompletion);

    public bool ContainsTarget(object target) => targets.Any(x => x.IsEqual(target));
}
