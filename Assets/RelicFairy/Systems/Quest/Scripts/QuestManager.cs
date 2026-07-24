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
    public event QuestCanceledHandler onQuestFailed;

    public event QuestRegisterHandler onAchievementRegistered;
    public event QuestCompletedHandler onAchievementCompleted;

    /// <summary>QuestDatabase 주입(Initialize) 완료 시 1회 발행. 비동기 부트스트랩 이후 등록을 거는 소비자용.</summary>
    public event Action onInitialized;
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

    /// <summary>QuestDatabase가 주입돼 RegisterQuest가 가능한 상태인지. (비동기 부트스트랩 완료 신호)</summary>
    public bool IsInitialized => _questDatabase != null;

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
            // 최초 부팅: 업적 전체 등록. 항목마다 저장하면 PlayerPrefs를 수십 번 쓰므로 끝에 1회만 저장한다.
            _suppressSave = true;
            foreach (var achievement in _achievementDatabase.Quests)
                Register(achievement);
            _suppressSave = false;
            Save();
        }

        onInitialized?.Invoke();
    }

    // ──────────────────────────────────────────────────────────
    // Public API
    // ──────────────────────────────────────────────────────────

    /// <param name="notify">
    /// false면 등장(onQuestRegistered) 이벤트를 발행하지 않는다. 세이브 <b>복원</b> 전용 —
    /// 복원은 '새로 수락'이 아닌데도 이벤트를 쏘면 QuestFeedbackPresenter가 등장 대사를 다시 재생해
    /// 게임을 켤 때마다 튜토리얼 안내가 반복된다.
    /// </param>
    public Quest Register(Quest quest, bool notify = true)
    {
        var newQuest = quest.Clone();

        if (newQuest is Achievement)
        {
            newQuest.onCompleted += OnAchievementCompleted;
            _activeAchievements.Add(newQuest);
            newQuest.OnRegister();
            if (notify) onAchievementRegistered?.Invoke(newQuest);
        }
        else
        {
            newQuest.onCompleted += OnQuestCompleted;
            newQuest.onCanceled  += OnQuestCanceled;
            _activeQuests.Add(newQuest);
            newQuest.OnRegister();
            if (notify) onQuestRegistered?.Invoke(newQuest);
        }

        if (notify) Save();   // 수락 상태를 즉시 영속화(복원 경로는 이미 저장본이므로 재저장 불필요)
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

    /// <summary>제한 시간 초과 등으로 퀘스트를 실패 처리. 활성 목록에서 제거하고 onQuestFailed 발행(보상/afterQuest 없음).</summary>
    public void FailQuest(Quest quest)
    {
        if (quest == null) return;
        if (_activeQuests.Remove(quest))
            onQuestFailed?.Invoke(quest);
    }

    public bool ContainInActiveQuests(Quest quest)
        => _activeQuests.Any(x => x.CodeName == quest.CodeName);

    public bool ContainInCompleteQuests(Quest quest)
        => _completedQuests.Any(x => x.CodeName == quest.CodeName);

    public bool ContainInActiveAchievement(Quest quest)
        => _activeAchievements.Any(x => x.CodeName == quest.CodeName);

    public bool ContainInCompleteAchievement(Quest quest)
        => _completedAchievements.Any(x => x.CodeName == quest.CodeName);

    // 일괄 등록(최초 부팅 업적 등록) 중 항목별 저장을 막는 가드. 끝에서 1회만 저장한다.
    private bool _suppressSave;

    public void Save()
    {
        if (_suppressSave) return;

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
        var newQuest = Register(quest, notify: false);   // 복원 — 등장 대사 재생 금지
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

    // 상태 전이(수락/완료/취소)마다 저장한다. 리포트마다 저장하면 처치 1회당 PlayerPrefs 쓰기가 되어
    // 비용이 크고, 정작 중요한 건 "이 퀘스트를 이미 받았/끝냈다"는 사실이다.

    private void OnQuestCompleted(Quest quest)
    {
        _activeQuests.Remove(quest);
        _completedQuests.Add(quest);
        onQuestCompleted?.Invoke(quest);
        Save();
    }

    private void OnQuestCanceled(Quest quest)
    {
        _activeQuests.Remove(quest);
        onQuestCanceled?.Invoke(quest);
        Save();
    }

    private void OnAchievementCompleted(Quest achievement)
    {
        _activeAchievements.Remove(achievement);
        _completedAchievements.Add(achievement);
        onAchievementCompleted?.Invoke(achievement);
        Save();
    }
}
