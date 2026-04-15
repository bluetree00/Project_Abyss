// using System.Collections;
// using System.Collections.Generic;
// using System.Diagnostics;
// using System.Linq;
// using UnityEngine;

// using Debug = UnityEngine.Debug;

// //  퀘스트 상태 이넘
// public enum QuestState
// {
//     Inactive,
//     Running,
//     Complete,
//     Cancel,
//     WaitingForCompletion
// }

// [CreateAssetMenu(menuName = "Quest/Quest", fileName = "Quest_")]
// public class Quest : ScriptableObject
// {

//     #region Events
//     public delegate void TaskSuccessChangedHandler(Quest quest, Task task, int currentSuccess, int prevSuccess);
//     public delegate void CompletedHandler(Quest quest);
//     public delegate void CanceledHandler(Quest quest);
//     public delegate void NewTaskGroupHandler(Quest quest, TaskGroup currentTaskGroup, TaskGroup prevTaskGroup);
//     #endregion
//     [SerializeField]
//     private Category category;
//     [SerializeField]
//     private Sprite icon;


//     [Header("Text")]
//     [SerializeField]
//     private string codeName;
//     [SerializeField]
//     private string displayName;
//     [SerializeField, TextArea]
//     private string description;

//     // Task 그룹 배열
//     [Header("Task")]
//     [SerializeField]
//     private TaskGroup[] taskGroups;

//     [Header("Reward")]
//     [SerializeField]
//     private Reward[] rewards;

//     [Header("Option")]
//     [SerializeField]
//     private bool useAutoComplete;
//     [SerializeField]
//     private bool isCancelable;
//     [SerializeField]
//     private bool isSavable;

//     [Header("Condition")]
//     [SerializeField]
//     private Condition[] acceptionConditions;
//     [SerializeField]
//     private Condition[] cancelConditions;

//     private int currentTaskGroupIndex;

//     // 프로퍼티 모음
//     public Category Category => category;
//     public Sprite Icon => icon;
//     public string CodeName => codeName;
//     public string DisplayName => displayName;
//     public string Description => description;
//     public QuestState State { get; private set; }
//     public TaskGroup CurrentTaskGroup => taskGroups[currentTaskGroupIndex];
//     public IReadOnlyList<TaskGroup> TaskGroups => taskGroups;
//     public IReadOnlyList<Reward> Rewards => rewards;

//     //퀘스트 상태에 따른 분류 위한 bool 모음

//     //예시 ↓ 퀘스트 상태가 Inactive 가 아니라면 IsRegistered 가 true
//     public bool IsRegistered => State != QuestState.Inactive;
//     public bool IsComplatable => State == QuestState.WaitingForCompletion;
//     public bool IsComplete => State == QuestState.Complete;
//     public bool IsCancel => State == QuestState.Cancel;
//     public virtual bool IsCancelable => isCancelable && cancelConditions.All(x => x.IsPass(this));
//     public bool IsAcceptable => acceptionConditions.All(x => x.IsPass(this));
//     public virtual bool IsSavable => isSavable;


//     // 이벤트 모음
//     public event TaskSuccessChangedHandler onTaskSuccessChanged;
//     public event CompletedHandler onCompleted;
//     public event CanceledHandler onCanceled;
//     public event NewTaskGroupHandler onNewTaskGroup;

//     public void OnRegister()
//     {
//         //Assert = 인자로 들어온 값이 false 일 경우 뒷 문장을 출력해줌
//         Debug.Assert(!IsRegistered, "This quest has already been registered");

//         foreach (var taskGroup in taskGroups)
//         {
//             taskGroup.Setup(this);
//             foreach (var task in taskGroup.Tasks)
//                 task.onSuccessChanged += OnSuccessChanged;
//         }

//         State = QuestState.Running;
//         CurrentTaskGroup.Start();
//     }

//     public void ReceiveReport(string category, object target, int successCount)
//     {
//         Debug.Assert(IsRegistered, "This quest has already been registered");
//         Debug.Assert(!IsCancel, "This quest has been cancaled");

//         if (IsComplete)
//             return;

//         CurrentTaskGroup.ReceiveReport(category, target, successCount);

//         if (CurrentTaskGroup.IsAllTaskComplete)
//         {
//             if (currentTaskGroupIndex + 1 == taskGroups.Length)
//             {
//                 State = QuestState.WaitingForCompletion;
//                 if (useAutoComplete)
//                     Complete();
//             }
//             else
//             {
//                 var prevTaskGroup = taskGroups[currentTaskGroupIndex++];
//                 prevTaskGroup.End();
//                 CurrentTaskGroup.Start();
//                 onNewTaskGroup?.Invoke(this, CurrentTaskGroup, prevTaskGroup);
//             }
//         }
//         else
//          State = QuestState.Running;
//     }

//     public void Complete()
//     {
//         CheckIsRunning();

//         foreach (var taskGroup in taskGroups)
//             taskGroup.Complete();

//         State = QuestState.Complete;

//         foreach (var reward in rewards)
//             reward.Give(this);

//         onCompleted?.Invoke(this);

//         onTaskSuccessChanged = null;
//         onCompleted = null;
//         onCanceled = null;
//         onNewTaskGroup = null;
//     }

//     public virtual void Cancel()
//     {
//         CheckIsRunning();
//         Debug.Assert(IsCancelable, "This quest can't be cancaled");

//         State = QuestState.Cancel;
//         onCanceled?.Invoke(this);
//     }

//     public Quest Clone()
//     {
//         var clone = Instantiate(this);
//         clone.taskGroups = taskGroups.Select(x => new TaskGroup(x)).ToArray();

//         return clone;
//     }

//     public QuestSaveData ToSaveData()
//     {
//         return new QuestSaveData
//         {
//             codeName = codeName,
//             state = State,
//             taskGroupIndex = currentTaskGroupIndex,
//             taskSuccessCounts = CurrentTaskGroup.Tasks.Select(x => x.CurrentSuccess).ToArray()
//         };
//     }

//     public void LoadFrom(QuestSaveData saveData)
//     {
//         State = saveData.state;
//         currentTaskGroupIndex = saveData.taskGroupIndex;

//         for (int i = 0; i < currentTaskGroupIndex; i++)
//         {
//             var taskGroup = taskGroups[i];
//             taskGroup.Complete();
//         }

//         for (int i = 0; i < saveData.taskSuccessCounts.Length; i++)
//         {
//             CurrentTaskGroup.Start();
//             CurrentTaskGroup.Tasks[i].CurrentSuccess = saveData.taskSuccessCounts[i];
//         }
//     }

//     private void OnSuccessChanged(Task task, int currentSuccess, int prevSuccess)
//         => onTaskSuccessChanged?.Invoke(this, task, currentSuccess, prevSuccess);

    
//     [Conditional("UNITY_EDITOR")]
//     private void CheckIsRunning()
//     {
//         Debug.Assert(IsRegistered, "This quest has already been registered");
//         Debug.Assert(!IsCancel, "This quest has been cancaled");
//         Debug.Assert(!IsComplete, "This quest has been already completed");
//     }
// }
