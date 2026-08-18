#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine;

/// <summary>
/// CSV → 완전 배선된 Quest/Achievement ScriptableObject 그래프 일괄 생성 도구.
/// 메뉴: RelicFairy/Gameplay/Quest/Generate From CSV
///
/// 고정 입력 경로: Assets/RelicFairy/Data/Quest/QuestDefinition.csv
/// 한 행 = 한 퀘스트(단일 태스크). 헤더 필수.
///
/// CSV 컬럼:
/// type,codeName,displayName,description,category,target,action,needCount,rewardType,rewardAmount,autoComplete,savable,chapter
///   type        : quest | achievement
///   category    : Kill / Room / Item / Gold ... (QuestEvents.Report의 category와 일치)
///   target      : 추적 대상 코드. "*" = 카테고리 내 전체
///   action      : SimpleCount / PositiveCount / NegativeCount / ContinuosCount / SimpleSet
///   rewardType  : Gold | None  (Item은 ItemSO 룩업 필요 — 후속)
///
/// 행마다 생성·배선: Category(재사용) · TaskAction(공유) · StringTarget · Task · Reward · Quest/Achievement
/// QuestDatabase / AchievementDatabase 에셋이 없으면 빈 상태로 생성 (채우기는 Rebuild Database).
/// </summary>
public static class QuestSOGenerator
{
    private const string kMenuPath   = "RelicFairy/Gameplay/Quest/Generate From CSV";
    private const string kCsvPath     = "Assets/RelicFairy/Data/Quest/QuestDefinition.csv";
    private const string kOutputRoot  = "Assets/RelicFairy/Data/Quest/Generated";

    // 생성한 퀘스트/업적 에셋 경로 ↔ 붙일 라벨. 배치 편집(StartAssetEditing) 중에는 GUID가 아직
    // 잡히지 않을 수 있어, Refresh 뒤에 한 번에 등록한다.
    private static readonly List<(string path, string label)> s_pendingLabels = new();

    [MenuItem(kMenuPath)]
    public static void GenerateFromCSV()
    {
        s_pendingLabels.Clear();
        string fullCsv = Path.Combine(Application.dataPath, "../", kCsvPath);
        if (!File.Exists(fullCsv))
        {
            Debug.LogError($"[QuestSOGenerator] CSV 없음: {kCsvPath}");
            return;
        }

        string[] lines = File.ReadAllLines(fullCsv, Encoding.UTF8);
        if (lines.Length < 2)
        {
            Debug.LogError("[QuestSOGenerator] CSV가 비어있거나 헤더만 있습니다.");
            return;
        }

        EnsureDirectory(kOutputRoot);
        EnsureDirectory($"{kOutputRoot}/Categories");
        EnsureDirectory($"{kOutputRoot}/Actions");
        EnsureDirectory($"{kOutputRoot}/Targets");
        EnsureDirectory($"{kOutputRoot}/Tasks");
        EnsureDirectory($"{kOutputRoot}/Rewards");
        EnsureDirectory($"{kOutputRoot}/Quests");
        EnsureDirectory($"{kOutputRoot}/Achievements");

        var createdByCode = new System.Collections.Generic.Dictionary<string, Quest>();
        var afterLinks    = new System.Collections.Generic.List<(string from, string to)>();

        AssetDatabase.StartAssetEditing();
        int quests = 0, achievements = 0, skipped = 0;
        try
        {
            for (int i = 1; i < lines.Length; i++)
            {
                string line = lines[i].Trim();
                if (string.IsNullOrEmpty(line)) continue;

                var cols = SplitCsvLine(line);
                if (cols.Count < 12)
                {
                    Debug.LogWarning($"[QuestSOGenerator] Line {i + 1}: 컬럼 수 부족({cols.Count}/12) — 건너뜀.");
                    skipped++;
                    continue;
                }

                string type        = cols[0].Trim().ToLowerInvariant();
                string codeName    = cols[1].Trim();
                string displayName = cols[2].Trim();
                string description = cols[3].Trim();
                string categoryStr = cols[4].Trim();
                string targetStr   = cols[5].Trim();
                string actionStr   = cols[6].Trim();
                int    needCount   = ParseInt(cols[7], 1);
                string rewardType  = cols[8].Trim();
                int    rewardAmt   = ParseInt(cols[9], 0);
                bool   autoComplete = ParseBool(cols[10], true);
                bool   savable      = ParseBool(cols[11], true);

                // 피드백(등장/완료 연출) — 선택 컬럼(idx 14~17), 없으면 None/빈문자
                string acceptTypeStr   = cols.Count >= 15 ? cols[14].Trim() : "";
                string acceptId        = cols.Count >= 16 ? cols[15].Trim() : "";
                string completeTypeStr = cols.Count >= 17 ? cols[16].Trim() : "";
                string completeId      = cols.Count >= 18 ? cols[17].Trim() : "";
                float  timeLimit       = cols.Count >= 19 ? ParseFloat(cols[18], 0f) : 0f;

                if (string.IsNullOrEmpty(codeName))
                {
                    Debug.LogWarning($"[QuestSOGenerator] Line {i + 1}: codeName 없음 — 건너뜀.");
                    skipped++;
                    continue;
                }

                bool isAchievement = type == "achievement";

                var category = GetOrCreateCategory(categoryStr);
                var action   = GetOrCreateAction(actionStr);
                var target   = CreateTarget(codeName, targetStr);
                var task     = CreateTask(codeName, description, category, action, target, needCount);
                var reward   = CreateReward(codeName, rewardType, rewardAmt);
                var quest    = CreateQuest(codeName, displayName, description, category, task, reward,
                                           autoComplete, savable, isAchievement,
                                           acceptTypeStr, acceptId, completeTypeStr, completeId, timeLimit);

                createdByCode[codeName] = quest;
                string afterCode = cols.Count >= 14 ? cols[13].Trim() : "";
                if (!string.IsNullOrEmpty(afterCode))
                    afterLinks.Add((codeName, afterCode));

                if (isAchievement) achievements++; else quests++;
            }
        }
        finally
        {
            AssetDatabase.StopAssetEditing();
        }

        // Pass 2 — afterQuest 체인 해석 (다음 퀘스트 에셋이 생성된 후 연결)
        foreach (var (from, to) in afterLinks)
        {
            if (!createdByCode.TryGetValue(from, out var fromQuest)) continue;
            if (!createdByCode.TryGetValue(to, out var toQuest))
            {
                Debug.LogWarning($"[QuestSOGenerator] afterQuest 대상 '{to}' 없음 (from '{from}') — 건너뜀.");
                continue;
            }
            var s = new SerializedObject(fromQuest);
            s.FindProperty("afterQuest").objectReferenceValue = toQuest;
            s.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(fromQuest);
        }

        // DB 에셋이 없으면 빈 상태로 생성 (채우기는 Rebuild Database 메뉴)
        EnsureDatabase($"{kOutputRoot}/QuestDatabase.asset");
        EnsureDatabase($"{kOutputRoot}/AchievementDatabase.asset");

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        ApplyAddressableLabels();
        AssetDatabase.SaveAssets();

        Debug.Log($"[QuestSOGenerator] 완료 — Quest {quests}, Achievement {achievements}, 건너뜀 {skipped} → {kOutputRoot}");
    }

    // ── 생성 헬퍼 ──────────────────────────────────────────────

    private static Category GetOrCreateCategory(string categoryStr)
    {
        string path = $"{kOutputRoot}/Categories/{Sanitize(categoryStr)}.asset";
        var existing = AssetDatabase.LoadAssetAtPath<Category>(path);
        if (existing != null) return existing;

        var so = ScriptableObject.CreateInstance<Category>();
        var s = new SerializedObject(so);
        s.FindProperty("codeName").stringValue    = categoryStr;
        s.FindProperty("displayName").stringValue = categoryStr;
        s.ApplyModifiedPropertiesWithoutUndo();
        AssetDatabase.CreateAsset(so, path);
        return so;
    }

    private static TaskAction GetOrCreateAction(string actionStr)
    {
        string token = string.IsNullOrEmpty(actionStr) ? "SimpleCount" : actionStr;
        string path = $"{kOutputRoot}/Actions/{Sanitize(token)}.asset";
        var existing = AssetDatabase.LoadAssetAtPath<TaskAction>(path);
        if (existing != null) return existing;

        TaskAction so = token.Replace(" ", "").ToLowerInvariant() switch
        {
            "positivecount"  => ScriptableObject.CreateInstance<PositiveCount>(),
            "negativecount"  => ScriptableObject.CreateInstance<NegativeCount>(),
            "continuoscount" => ScriptableObject.CreateInstance<ContinuosCount>(),
            "continuouscount" => ScriptableObject.CreateInstance<ContinuosCount>(),
            "simpleset"      => ScriptableObject.CreateInstance<SimpleSet>(),
            _                => ScriptableObject.CreateInstance<SimpleCount>(),
        };
        AssetDatabase.CreateAsset(so, path);
        return so;
    }

    private static StringTarget CreateTarget(string codeName, string targetStr)
    {
        string path = $"{kOutputRoot}/Targets/Target_{Sanitize(codeName)}.asset";
        var so = ScriptableObject.CreateInstance<StringTarget>();
        var s = new SerializedObject(so);
        s.FindProperty("value").stringValue = targetStr;
        s.ApplyModifiedPropertiesWithoutUndo();
        CreateOrReplace(so, path);
        return so;
    }

    private static Task CreateTask(string codeName, string description, Category category,
                                   TaskAction action, StringTarget target, int needCount)
    {
        string path = $"{kOutputRoot}/Tasks/{Sanitize(codeName)}_Task.asset";
        var so = ScriptableObject.CreateInstance<Task>();
        var s = new SerializedObject(so);
        s.FindProperty("codeName").stringValue           = codeName;
        s.FindProperty("description").stringValue         = description;
        s.FindProperty("category").objectReferenceValue   = category;
        s.FindProperty("action").objectReferenceValue     = action;
        s.FindProperty("needSuccessToComplete").intValue  = needCount;
        var targetsProp = s.FindProperty("targets");
        targetsProp.arraySize = 1;
        targetsProp.GetArrayElementAtIndex(0).objectReferenceValue = target;
        s.ApplyModifiedPropertiesWithoutUndo();
        CreateOrReplace(so, path);
        return so;
    }

    private static Reward CreateReward(string codeName, string rewardType, int amount)
    {
        if (string.IsNullOrEmpty(rewardType) || rewardType.ToLowerInvariant() == "none")
            return null;

        string kind = rewardType.ToLowerInvariant();
        string path = $"{kOutputRoot}/Rewards/{Sanitize(codeName)}_Reward.asset";

        // Essence — 업적의 정본 보상. 계정 영구 재화라 런이 없는 거점에서도 지급된다.
        if (kind == "essence")
        {
            var eso = ScriptableObject.CreateInstance<EssenceReward>();
            var es = new SerializedObject(eso);
            es.FindProperty("essenceAmount").intValue = amount;
            es.FindProperty("quantity").intValue      = amount;
            es.FindProperty("description").stringValue = $"심연의 정수 {amount}";
            es.ApplyModifiedPropertiesWithoutUndo();
            CreateOrReplace(eso, path);
            return eso;
        }

        // Gold는 <b>런 재화</b>라 인런 퀘스트 전용이다. 업적에 붙이면 거점 수령 시 런이 없어 증발한다.
        if (kind != "gold")
        {
            Debug.LogWarning($"[QuestSOGenerator] '{codeName}': rewardType '{rewardType}' 미지원 — 보상 없이 생성.");
            return null;
        }

        var so = ScriptableObject.CreateInstance<GoldReward>();
        var s = new SerializedObject(so);
        s.FindProperty("goldAmount").intValue = amount;
        s.FindProperty("quantity").intValue   = amount;
        s.FindProperty("description").stringValue = $"골드 {amount}";
        s.ApplyModifiedPropertiesWithoutUndo();
        CreateOrReplace(so, path);
        return so;
    }

    private static Quest CreateQuest(string codeName, string displayName, string description,
                                     Category category, Task task, Reward reward,
                                     bool autoComplete, bool savable, bool isAchievement,
                                     string acceptType, string acceptId,
                                     string completeType, string completeId, float timeLimit)
    {
        string folder = isAchievement ? "Achievements" : "Quests";
        string path = $"{kOutputRoot}/{folder}/{Sanitize(codeName)}.asset";
        Quest so = isAchievement
            ? ScriptableObject.CreateInstance<Achievement>()
            : ScriptableObject.CreateInstance<Quest>();

        var s = new SerializedObject(so);
        s.FindProperty("codeName").stringValue         = codeName;
        s.FindProperty("displayName").stringValue       = displayName;
        s.FindProperty("description").stringValue        = description;
        s.FindProperty("category").objectReferenceValue  = category;
        s.FindProperty("useAutoCompletion").boolValue    = autoComplete;
        s.FindProperty("isSavable").boolValue            = savable;
        s.FindProperty("isCancelable").boolValue         = false;

        s.FindProperty("acceptFeedbackType").enumValueIndex   = ParseFeedbackType(acceptType);
        s.FindProperty("acceptFeedbackId").stringValue        = acceptId;
        s.FindProperty("completeFeedbackType").enumValueIndex = ParseFeedbackType(completeType);
        s.FindProperty("completeFeedbackId").stringValue      = completeId;
        s.FindProperty("timeLimit").floatValue                = timeLimit;

        // TaskGroup[] (직렬화 중첩 클래스) — 1그룹 1태스크 배선
        var groupsProp = s.FindProperty("taskGroups");
        groupsProp.arraySize = 1;
        var tasksProp = groupsProp.GetArrayElementAtIndex(0).FindPropertyRelative("tasks");
        tasksProp.arraySize = 1;
        tasksProp.GetArrayElementAtIndex(0).objectReferenceValue = task;

        // Reward[]
        var rewardsProp = s.FindProperty("rewards");
        if (reward != null)
        {
            rewardsProp.arraySize = 1;
            rewardsProp.GetArrayElementAtIndex(0).objectReferenceValue = reward;
        }
        else
        {
            rewardsProp.arraySize = 0;
        }

        s.ApplyModifiedPropertiesWithoutUndo();
        CreateOrReplace(so, path);
        // 런타임은 이 라벨로 목록을 모은다 — 여기서 붙여야 CSV에 행을 추가하는 것만으로 게임에 반영된다.
        s_pendingLabels.Add((path, isAchievement ? QuestLabels.Achievement : QuestLabels.Quest));
        return so;
    }

    /// <summary>
    /// 이미 생성돼 있는 에셋에 라벨만 다시 붙인다. 재생성(=GUID 교체) 없이 고칠 때 쓴다.
    /// </summary>
    [MenuItem("RelicFairy/Gameplay/Quest/Apply Addressable Labels")]
    public static void ApplyLabelsToExisting()
    {
        s_pendingLabels.Clear();
        Collect($"{kOutputRoot}/Quests",       QuestLabels.Quest);
        Collect($"{kOutputRoot}/Achievements", QuestLabels.Achievement);
        ApplyAddressableLabels();
        AssetDatabase.SaveAssets();

        static void Collect(string folder, string label)
        {
            foreach (var guid in AssetDatabase.FindAssets("t:Quest", new[] { folder }))
                s_pendingLabels.Add((AssetDatabase.GUIDToAssetPath(guid), label));
        }
    }

    /// <summary>생성된 퀘스트/업적을 어드레서블 기본 그룹에 등록하고 라벨을 붙인다.</summary>
    private static void ApplyAddressableLabels()
    {
        var settings = AddressableAssetSettingsDefaultObject.Settings;
        if (settings == null)
        {
            Debug.LogError("[QuestSOGenerator] AddressableAssetSettings 없음 — 라벨 등록 실패. " +
                           "라벨이 없으면 런타임 목록이 비어 퀘스트·업적이 하나도 뜨지 않는다.");
            return;
        }

        settings.AddLabel(QuestLabels.Quest, false);
        settings.AddLabel(QuestLabels.Achievement, false);

        int count = 0;
        foreach (var (path, label) in s_pendingLabels)
        {
            string guid = AssetDatabase.AssetPathToGUID(path);
            if (string.IsNullOrEmpty(guid))
            {
                Debug.LogWarning($"[QuestSOGenerator] GUID 없음 — 라벨 건너뜀: {path}");
                continue;
            }

            var entry = settings.CreateOrMoveEntry(guid, settings.DefaultGroup, false, false);
            entry.SetLabel(label, true, false, false);
            count++;
        }

        settings.SetDirty(AddressableAssetSettings.ModificationEvent.EntryMoved, null, true);
        Debug.Log($"[QuestSOGenerator] 어드레서블 라벨 {count}건 등록.");
    }

    private static void EnsureDatabase(string path)
    {
        if (AssetDatabase.LoadAssetAtPath<QuestDatabase>(path) != null) return;
        var db = ScriptableObject.CreateInstance<QuestDatabase>();
        AssetDatabase.CreateAsset(db, path);
    }

    // ── 유틸 ──────────────────────────────────────────────────

    private static void CreateOrReplace(ScriptableObject so, string path)
    {
        var existing = AssetDatabase.LoadAssetAtPath<ScriptableObject>(path);
        if (existing != null)
            AssetDatabase.DeleteAsset(path);
        AssetDatabase.CreateAsset(so, path);
    }

    private static void EnsureDirectory(string path)
    {
        string full = Path.Combine(Application.dataPath, "../", path);
        if (!Directory.Exists(full))
        {
            Directory.CreateDirectory(full);
            AssetDatabase.ImportAsset(path);
        }
    }

    private static List<string> SplitCsvLine(string line)
    {
        var result = new List<string>();
        var sb = new StringBuilder();
        bool inQuotes = false;
        foreach (char c in line)
        {
            if (c == '"') inQuotes = !inQuotes;
            else if (c == ',' && !inQuotes) { result.Add(sb.ToString()); sb.Clear(); }
            else sb.Append(c);
        }
        result.Add(sb.ToString());
        return result;
    }

    private static int ParseInt(string s, int fallback)
        => int.TryParse(s.Trim(), out int v) ? v : fallback;

    private static bool ParseBool(string s, bool fallback)
        => bool.TryParse(s.Trim(), out bool v) ? v : fallback;

    private static float ParseFloat(string s, float fallback)
        => float.TryParse(s.Trim(), out float v) ? v : fallback;

    // QuestFeedbackType enum 인덱스(None=0, Text=1, Dialogue=2)
    private static int ParseFeedbackType(string s)
    {
        switch (s.Trim().ToLowerInvariant())
        {
            case "dialogue": return 2;
            case "text":     return 1;
            default:         return 0;
        }
    }

    private static string Sanitize(string s)
    {
        foreach (char c in Path.GetInvalidFileNameChars())
            s = s.Replace(c, '_');
        return s.Replace(' ', '_');
    }
}
#endif
