using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;

/// <summary>
/// 퀘스트/업적 시스템 매니저. 순수 C# 클래스 — Managers 서비스 로케이터로 접근.
/// QuestEvents 정적 버스를 구독해 ReceiveReport를 자동 라우팅한다.
/// </summary>
public class QuestManager
{
    #region Save Keys
    private const string kSaveRootPath             = "questSystem";
    private const string kActiveQuestsSavePath      = "activeQuests";
    private const string kCompletedQuestsSavePath   = "completedQuests";
    private const string kActiveAchievementsSavePath    = "activeAchievements";
    private const string kCompletedAchievementsSavePath = "completedAchievements";
    #endregion

    #region Events
    public delegate void QuestRegisterHandler(Quest quest);
    public delegate void QuestCompletedHandler(Quest quest);
    public delegate void QuestCanceledHandler(Quest quest);

    public event QuestRegisterHandler onQuestRegistered;
    public event QuestCompletedHandler onQuestCompleted;
    public event QuestCanceledHandler onQuestCanceled;

    public event QuestRegisterHandler onAchievementRegistered;
    public event QuestCompletedHandler onAchievementCompleted;
    #endregion

    // Singleton accessor — routed through Managers service locator
    public static QuestManager Instance => Managers.Quest;

    private readonly List<Quest> _activeQuests      = new List<Quest>();
    private readonly List<Quest> _completedQuests    = new List<Quest>();
    private readonly List<Quest> _activeAchievements    = new List<Quest>();
    private readonly List<Quest> _completedAchievements = new List<Quest>();

    private QuestDatabase _questDatabase;
    private QuestDatabase _achievementDatabase;

    public IReadOnlyList<Quest> ActiveQuests        => _activeQuests;
    public IReadOnlyList<Quest> CompletedQuests     => _completedQuests;
    public IReadOnlyList<Quest> ActiveAchievements  => _activeAchievements;
    public IReadOnlyList<Quest> CompletedAchievements => _completedAchievements;

    public QuestManager()
    {
        SubscribeQuestEvents();
    }

    // ──────────────────────────────────────────────────────────
    // Initialise — call from Bootstrapper after Addressables ready
    // ──────────────────────────────────────────────────────────

    public void Initialize(QuestDatabase questDb, QuestDatabase achievementDb)
    {
        _questDatabase       = questDb;
        _achievementDatabase = achievementDb;

        if (!Load())
        {
            foreach (var achievement in _achievementDatabase.Quests)
                Register(achievement);
        }
    }

    // ──────────────────────────────────────────────────────────
    // Public API
    // ──────────────────────────────────────────────────────────

    public Quest Register(Quest quest)
    {
        var newQuest = quest.Clone();

        if (newQuest is Achievement)
        {
            newQuest.onCompleted += OnAchievementCompleted;
            _activeAchievements.Add(newQuest);
            newQuest.OnRegister();
            onAchievementRegistered?.Invoke(newQuest);
        }
        else
        {
            newQuest.onCompleted += OnQuestCompleted;
            newQuest.onCanceled  += OnQuestCanceled;
            _activeQuests.Add(newQuest);
            newQuest.OnRegister();
            onQuestRegistered?.Invoke(newQuest);
        }

        return newQuest;
    }

    /// <summary>
    /// QuestDatabase에서 codeName으로 퀘스트를 찾아 등록. 이미 진행/완료 중이면 무시.
    /// 런 시작·튜토리얼 등에서 특정 퀘스트를 명시적으로 시작할 때 사용.
    /// </summary>
    public Quest RegisterQuest(string codeName)
    {
        if (_questDatabase == null)
        {
            Debug.LogWarning("[QuestManager] QuestDatabase 미초기화 — RegisterQuest 무시.");
            return null;
        }

        var quest = _questDatabase.FindQuestBy(codeName);
        if (quest == null)
        {
            Debug.LogWarning($"[QuestManager] '{codeName}' 퀘스트를 DB에서 찾을 수 없음.");
            return null;
        }

        if (ContainInActiveQuests(quest) || ContainInCompleteQuests(quest))
            return null;

        return Register(quest);
    }

    public void ReceiveReport(string category, object target, int successCount)
    {
        ReceiveReport(_activeQuests, category, target, successCount);
        ReceiveReport(_activeAchievements, category, target, successCount);
    }

    public void ReceiveReport(Category category, TaskTarget target, int successCount)
        => ReceiveReport(category.CodeName, target.Value, successCount);

    public void CompleteWaitingQuests()
    {
        foreach (var quest in _activeQuests.ToList())
        {
            if (quest.IsCompltable)
                quest.Complete();
        }
    }

    public bool ContainInActiveQuests(Quest quest)
        => _activeQuests.Any(x => x.CodeName == quest.CodeName);

    public bool ContainInCompleteQuests(Quest quest)
        => _completedQuests.Any(x => x.CodeName == quest.CodeName);

    public bool ContainInActiveAchievement(Quest quest)
        => _activeAchievements.Any(x => x.CodeName == quest.CodeName);

    public bool ContainInCompleteAchievement(Quest quest)
        => _completedAchievements.Any(x => x.CodeName == quest.CodeName);

    public void Save()
    {
        var root = new JObject();
        root.Add(kActiveQuestsSavePath,         CreateSaveData(_activeQuests));
        root.Add(kCompletedQuestsSavePath,      CreateSaveData(_completedQuests));
        root.Add(kActiveAchievementsSavePath,   CreateSaveData(_activeAchievements));
        root.Add(kCompletedAchievementsSavePath, CreateSaveData(_completedAchievements));

        PlayerPrefs.SetString(kSaveRootPath, root.ToString());
        PlayerPrefs.Save();
    }

    // ──────────────────────────────────────────────────────────
    // QuestEvents subscription
    // ──────────────────────────────────────────────────────────

    private void SubscribeQuestEvents()
    {
        // 단일 범용 채널만 구독 — 새 카테고리는 여기 수정 없이 Report(category,...)로 확장된다.
        QuestEvents.OnReported += ReceiveReport;
    }

    // ──────────────────────────────────────────────────────────
    // Private helpers
    // ──────────────────────────────────────────────────────────

    private void ReceiveReport(List<Quest> quests, string category, object target, int successCount)
    {
        foreach (var quest in quests.ToArray())
            quest.ReceiveReport(category, target, successCount);
    }

    private JArray CreateSaveData(IReadOnlyList<Quest> quests)
    {
        var saveDatas = new JArray();
        foreach (var quest in quests)
        {
            if (quest.IsSavable)
                saveDatas.Add(JObject.FromObject(quest.ToSaveData()));
        }
        return saveDatas;
    }

    private bool Load()
    {
        if (!PlayerPrefs.HasKey(kSaveRootPath)) return false;

        var root = JObject.Parse(PlayerPrefs.GetString(kSaveRootPath));

        LoadSaveDatas(root[kActiveQuestsSavePath],         _questDatabase,       LoadActiveQuest);
        LoadSaveDatas(root[kCompletedQuestsSavePath],      _questDatabase,       LoadCompletedQuest);
        LoadSaveDatas(root[kActiveAchievementsSavePath],   _achievementDatabase, LoadActiveQuest);
        LoadSaveDatas(root[kCompletedAchievementsSavePath], _achievementDatabase, LoadCompletedQuest);

        return true;
    }

    private void LoadSaveDatas(JToken datasToken, QuestDatabase database, Action<QuestSaveData, Quest> onSuccess)
    {
        if (datasToken is not JArray datas) return;
        foreach (var data in datas)
        {
            var saveData = data.ToObject<QuestSaveData>();
            var quest = database?.FindQuestBy(saveData.codeName);
            if (quest != null)
                onSuccess.Invoke(saveData, quest);
        }
    }

    private void LoadActiveQuest(QuestSaveData saveData, Quest quest)
    {
        var newQuest = Register(quest);
        newQuest.LoadFrom(saveData);
    }

    private void LoadCompletedQuest(QuestSaveData saveData, Quest quest)
    {
        var newQuest = quest.Clone();
        newQuest.LoadFrom(saveData);

        if (newQuest is Achievement)
            _completedAchievements.Add(newQuest);
        else
            _completedQuests.Add(newQuest);
    }

    // ──────────────────────────────────────────────────────────
    // Callbacks
    // ──────────────────────────────────────────────────────────

    private void OnQuestCompleted(Quest quest)
    {
        _activeQuests.Remove(quest);
        _completedQuests.Add(quest);
        onQuestCompleted?.Invoke(quest);
    }

    private void OnQuestCanceled(Quest quest)
    {
        _activeQuests.Remove(quest);
        onQuestCanceled?.Invoke(quest);
    }

    private void OnAchievementCompleted(Quest achievement)
    {
        _activeAchievements.Remove(achievement);
        _completedAchievements.Add(achievement);
        onAchievementCompleted?.Invoke(achievement);
    }
}
