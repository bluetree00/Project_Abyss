using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using UnityEngine;

using Debug = UnityEngine.Debug;

public enum QuestState
{
    Inactive,
    Running,
    Complete,
    Cancel,
    WaitingForCompletion
}

/// <summary>퀘스트 이벤트(등장/완료) 시 재생할 피드백 종류. None=없음, Text=가벼운 배너/토스트, Dialogue=대사 팝업.</summary>
public enum QuestFeedbackType
{
    None,
    Text,
    Dialogue
}

[CreateAssetMenu(menuName = "Quest/Quest", fileName = "Quest_")]
public class Quest : ScriptableObject
{
    #region Events
    public delegate void TaskSuccessChangedHandler(Quest quest, Task task, int currentSuccess, int prevSuccess);
    public delegate void CompletedHandler(Quest quest);
    public delegate void CanceledHandler(Quest quest);
    public delegate void NewTaskGroupHandler(Quest quest, TaskGroup currentTaskGroup, TaskGroup prevTaskGroup);
    #endregion

    [SerializeField] private Category category;
    [SerializeField] private Sprite icon;

    [Header("Text")]
    [SerializeField] private string codeName;
    [SerializeField] private string displayName;
    [SerializeField, TextArea] private string description;

    [Header("Task")]
    [SerializeField] private TaskGroup[] taskGroups;

    [Header("Reward")]
    [SerializeField] private Reward[] rewards;

    [Header("Option")]
    [SerializeField] private bool useAutoCompletion;
    [SerializeField] private bool isCancelable;
    [SerializeField] private bool isSavable;

    [Header("Condition")]
    [SerializeField] private Condition[] acceptionConditions;
    [SerializeField] private Condition[] cancelConditions;

    [Header("완료 후 다음 퀘스트가 있다면")]
    [SerializeField] private Quest afterQuest;

    [Header("Feedback (등장/완료 시 연출)")]
    [SerializeField] private QuestFeedbackType acceptFeedbackType;
    [SerializeField] private string acceptFeedbackId;
    [SerializeField] private QuestFeedbackType completeFeedbackType;
    [SerializeField] private string completeFeedbackId;

    [Header("제한 시간 (초, 0=무제한 — 돌발/타임어택 퀘스트용)")]
    [SerializeField] private float timeLimit;

    private int _currentTaskGroupIndex;

    public Category Category => category;
    public Sprite Icon => icon;
    public string CodeName => codeName;
    public string DisplayName => displayName;
    public string Description => description;
    public QuestState State { get; private set; }

    public TaskGroup CurrentTaskGroup => taskGroups[_currentTaskGroupIndex];
    public IReadOnlyList<TaskGroup> TaskGroups => taskGroups;
    public IReadOnlyList<Reward> Rewards => rewards;

    public QuestFeedbackType AcceptFeedbackType   => acceptFeedbackType;
    public string            AcceptFeedbackId     => acceptFeedbackId;
    public QuestFeedbackType CompleteFeedbackType => completeFeedbackType;
    public string            CompleteFeedbackId   => completeFeedbackId;
    public float             TimeLimit            => timeLimit;

    public bool IsRegistered => State == QuestState.Inactive;
    public bool IsCompltable => State == QuestState.WaitingForCompletion;
    public bool IsComplete => State == QuestState.Complete;
    public bool IsCancel => State == QuestState.Cancel;
    public virtual bool IsCancelable => isCancelable && cancelConditions.All(x => x.IsPass(this));
    public bool IsAcceptable => acceptionConditions.All(x => x.IsPass(this));
    public virtual bool IsSavable => isSavable;

    public event TaskSuccessChangedHandler onTaskSuccessChanged;
    public event CompletedHandler onCompleted;
    public event CanceledHandler onCanceled;
    public event NewTaskGroupHandler onNewTaskGroup;

    public void OnRegister()
    {
        Debug.Assert(IsRegistered, "This quest has already been registered");

        foreach (var taskGroup in taskGroups)
        {
            taskGroup.Setup(this);
            foreach (var task in taskGroup.Tasks)
                task.onSuccessChanged += OnSuccessChanged;
        }

        State = QuestState.Running;
        CurrentTaskGroup.Start();
    }

    public void ReceiveReport(string category, object target, int successCount)
    {
        Debug.Assert(!IsRegistered, "This quest has not been registered");
        Debug.Assert(!IsCancel, "This quest has been canceled.");

        if (IsComplete) return;

        CurrentTaskGroup.ReceiveReport(category, target, successCount);

        if (CurrentTaskGroup.IsAllTaskComplete)
        {
            if (_currentTaskGroupIndex + 1 == taskGroups.Length)
            {
                State = QuestState.WaitingForCompletion;
                if (useAutoCompletion)
                    Complete();
            }
            else
            {
                var prevTaskGroup = taskGroups[_currentTaskGroupIndex++];
                prevTaskGroup.End();
                CurrentTaskGroup.Start();
                onNewTaskGroup?.Invoke(this, CurrentTaskGroup, prevTaskGroup);
            }
        }
        else
        {
            State = QuestState.Running;
        }
    }

    public void Complete()
    {
        CheckIsRunning();

        foreach (var taskGroup in taskGroups)
            taskGroup.Complete();

        State = QuestState.Complete;

        foreach (var reward in rewards)
            reward.Give(this);

        // 완료 이벤트를 afterQuest 등록보다 먼저 발행 → 트래커가 "완료 연출 → 다음 퀘스트 등장" 순서로 처리.
        onCompleted?.Invoke(this);

        if (afterQuest != null)
            QuestManager.Instance?.Register(afterQuest);

        onTaskSuccessChanged = null;
        onCompleted = null;
        onCanceled = null;
        onNewTaskGroup = null;
    }

    public virtual void Cancel()
    {
        CheckIsRunning();
        Debug.Assert(IsCancelable, "This quest can't be canceled.");

        State = QuestState.Cancel;
        onCanceled?.Invoke(this);
    }

    public bool ContainsTarget(object target) => taskGroups.Any(x => x.ContainsTarget(target));
    public bool ContainsTarget(TaskTarget target) => ContainsTarget(target.Value);

    public Quest Clone()
    {
        var clone = Instantiate(this);
        clone.taskGroups = taskGroups.Select(x => new TaskGroup(x)).ToArray();
        return clone;
    }

    public QuestSaveData ToSaveData()
    {
        return new QuestSaveData
        {
            codeName = codeName,
            state = State,
            taskGroupIndex = _currentTaskGroupIndex,
            taskSuccessCounts = CurrentTaskGroup.Tasks.Select(x => x.CurrentSuccess).ToArray()
        };
    }

    public void LoadFrom(QuestSaveData saveData)
    {
        State = saveData.state;
        _currentTaskGroupIndex = saveData.taskGroupIndex;

        for (int i = 0; i < _currentTaskGroupIndex; i++)
            taskGroups[i].Complete();

        for (int i = 0; i < saveData.taskSuccessCounts.Length; i++)
        {
            CurrentTaskGroup.Start();
            CurrentTaskGroup.Tasks[i].CurrentSuccess = saveData.taskSuccessCounts[i];
        }
    }

    private void OnSuccessChanged(Task task, int currentSuccess, int prevSuccess)
        => onTaskSuccessChanged?.Invoke(this, task, currentSuccess, prevSuccess);

    [Conditional("UNITY_EDITOR")]
    private void CheckIsRunning()
    {
        Debug.Assert(!IsRegistered, "This quest has not been registered");
        Debug.Assert(!IsCancel, "This quest has been canceled.");
        Debug.Assert(!IsComplete, "This quest has been already completed");
    }
}
