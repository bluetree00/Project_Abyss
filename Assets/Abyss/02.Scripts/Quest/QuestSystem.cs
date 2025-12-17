// using System.Collections;
// using System.Collections.Generic;
// using System.Linq;
// using UnityEngine;
// using Newtonsoft.Json.Linq;
// using UnityEditor;

// public class QuestSystem : MonoBehaviour
// {

//     #region Save Path
//     private const string kSaveRootPath = "questSystem";
//     private const string kActiveQuestsSavePath = "activeQuests";
//     private const string kCompletedQuestsSavePath = "completedQuests";
//     private const string kActiveAchievementsSavePath = "activeAchievements";    
//     private const string kCompletedAchievementsSavePath = "completedAchievements";

//     #endregion

//     #region Events
//     public delegate void QuestRegisterHandler(Quest quest);
//     public delegate void QuestCompletedHandler(Quest quest);
//     public delegate void QuestCanceledHandler(Quest quest);
//     #endregion
//     private static QuestSystem instance;
//     private static bool isApplicationQuitting;

//     public static QuestSystem Instance
//     {
//         get
//         {
//             if(!isApplicationQuitting && instance == null)
//             {
//                 instance = FindObjectOfType<QuestSystem>();
//                 if(instance == null)
//                 {
//                     instance = new GameObject("Quest System").AddComponent<QuestSystem>();
//                     DontDestroyOnLoad(instance.gameObject);
//                 }
//             }
//             return instance;
//         }
//     }

//     [SerializeField]
//     private List<Quest> activeQuests = new List<Quest>();
//     [SerializeField]
//     private List<Quest> completedQuests = new List<Quest>();
//     [SerializeField]
//     private List<Quest> activeAchievements = new List<Quest>();
//     [SerializeField]
//     private List<Quest> completedAchievements = new List<Quest>();

//     private QuestDatabase questDatabase;
//     private QuestDatabase achievementDatabase;

//     public event QuestRegisterHandler onQuestRegistered;
//     public event QuestCompletedHandler onQuestCompleted;
//     public event QuestCanceledHandler onQuestCanceled;

//     public event QuestRegisterHandler onAchievementRegistered;
//     public event QuestCompletedHandler onAchievementCompleted;

//     public IReadOnlyList<Quest> ActiveQuests => activeQuests;
//     public IReadOnlyList<Quest> CompletedQuests => completedQuests;

//     public IReadOnlyList<Quest> ActiveAchievements => activeAchievements;
//     public IReadOnlyList<Quest> CompletedAchievements => completedAchievements;

//     private void Awake() {
//         questDatabase = Resources.Load<QuestDatabase>("Quest Database");
//         achievementDatabase = Resources.Load<QuestDatabase>("Achievement Database");

//         if(!Load())
//         {
//             foreach(var achievement in achievementDatabase.Quests)
//                 Register(achievement);
//         }
//     }

//     private void OnApplicationQuit() 
//     {
//         isApplicationQuitting = true;
//         //Save();
//     }

//     public Quest Register(Quest quest)
//     {
//         var newQuest = quest.Clone();

//         if(newQuest is Achievement)
//         {
//             newQuest.onCompleted += OnAchievementCompleted;

//             activeAchievements.Add(newQuest);

//             newQuest.OnRegister();

//             onAchievementRegistered?.Invoke(newQuest);
//         }
//         else
//         {
//             newQuest.onCompleted += OnQuestCompleted;
//             newQuest.onCanceled += OnQuestCanceled;

//             activeQuests.Add(newQuest);

//             //newQuest.Initialize();
//             newQuest.OnRegister();
//             onQuestRegistered?.Invoke(newQuest);
//         }

//         return newQuest;
//     }

//     public void ReceiveReport(string category, object target, int successCount)
//     {
//         ReceiveReport(activeQuests, category, target, successCount);
//         ReceiveReport(activeAchievements, category, target, successCount);
//     }

//     public void ReceiveReport(Category category, TaskTarget target, int successCount)
//         => ReceiveReport(category.CodeName, target.Value, successCount);

//     private void ReceiveReport(List<Quest> quests, string category, object target, int successCount)
//     {
//         foreach(var quest in quests.ToArray())
//             quest.ReceiveReport(category, target, successCount);
//     }

//     public void CompleteWaitingQuests()     //퀘스트 들중 완료가능한 퀘스트 중 완료 대기중인 퀘스트 완료시키는 함수
//     {
//         foreach (var quest in activeQuests.ToList())
//         {
//             if (quest.IsComplatable)
//                 quest.Complete();
//         }
//     }       

//     public bool ContainInActiveQuests(Quest quest) => activeQuests.Any(x => x.CodeName == quest.CodeName);
//     public bool ContainInCompleteQuests(Quest quest) => completedQuests.Any(x => x.CodeName == quest.CodeName);

//     public bool ContainInActiveAchievement(Quest quest) => activeAchievements.Any(x => x.CodeName == quest.CodeName);
//     public bool ContainInCompleteAchievement(Quest quest) => completedAchievements.Any(x => x.CodeName == quest.CodeName);

//     private void Save()
//     {
//         var root = new JObject();
//         root.Add(kActiveQuestsSavePath, CreatSaveData(activeQuests));
//         root.Add(kCompletedQuestsSavePath, CreatSaveData(completedQuests));
//         root.Add(kActiveAchievementsSavePath, CreatSaveData(activeAchievements));
//         root.Add(kCompletedAchievementsSavePath, CreatSaveData(completedAchievements));

//         PlayerPrefs.SetString(kSaveRootPath, root.ToString());
//         PlayerPrefs.Save();
//     }

//     private bool Load()
//     {
//         if(PlayerPrefs.HasKey(kSaveRootPath))
//         {
//             var root = JObject.Parse(PlayerPrefs.GetString(kSaveRootPath));

//             LoadSavaDatas(root[kActiveQuestsSavePath], questDatabase, LoadActiveQuest);
//             LoadSavaDatas(root[kCompletedQuestsSavePath], questDatabase, LoadCompletedQuest);

//             LoadSavaDatas(root[kActiveAchievementsSavePath], achievementDatabase, LoadActiveQuest);
//             LoadSavaDatas(root[kCompletedAchievementsSavePath], achievementDatabase, LoadCompletedQuest);

//             return true;
//         }
//         else
//             return false;
//     }

//     //Json으로 퀘스트 데이터를 저장
//     private JArray CreatSaveData(IReadOnlyList<Quest> quests)
//     {
//         var saveDatas = new JArray();
//         foreach(var quest in quests)
//         {
//             if(quest.IsSavable)
//                 saveDatas.Add(JObject.FromObject(quest.ToSaveData()));
//         }
//         return saveDatas;
//     } 

//     //Token = 위에서 만들어진 세이브 데이터가 들어갈 변수
//     private void LoadSavaDatas(JToken datasToken, QuestDatabase database, System.Action<QuestSaveData, Quest> onSuccess)
//     {
//         var datas = datasToken as JArray;
//         foreach(var data in datas)
//         {
//             var saveData = data.ToObject<QuestSaveData>();
//             var quest = database.FindQuestBy(saveData.codeName);
//             onSuccess.Invoke(saveData, quest);
//         }
//     }
//     //로드된 데이터 중 진행중인 퀘스트 등록
//     private void LoadActiveQuest(QuestSaveData saveData, Quest quest)
//     {
//         var newQuest = Register(quest);
//         newQuest.LoadFrom(saveData);
//     }
//     //로드된 데이터 중 완료된 퀘스트 완료목록에 저장
//     private void LoadCompletedQuest(QuestSaveData saveData, Quest quest)
//     {
//         var newQuest = quest.Clone();
//         newQuest.LoadFrom(saveData);

//         if(newQuest is Achievement)
//             completedAchievements.Add(newQuest);
//         else
//             completedQuests.Add(newQuest);
//     }

//     #region Callback
    
//     private void OnQuestCompleted(Quest quest)
//     {
//         activeQuests.Remove(quest);
//         completedQuests.Add(quest);

//         onQuestCompleted?.Invoke(quest);
//     }

//     private void OnQuestCanceled(Quest quest)
//     {
//         activeQuests.Remove(quest);
//         onQuestCanceled?.Invoke(quest);

//         Destroy(quest, Time.deltaTime);
//     }

//     private void OnAchievementCompleted(Quest achievement)
//     {
//         activeAchievements.Remove(achievement);
//         completedAchievements.Add(achievement);

//         onAchievementCompleted?.Invoke(achievement);
//     }

//     #endregion

    
// }
