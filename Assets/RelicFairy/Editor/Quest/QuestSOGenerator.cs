#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
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

    [MenuItem(kMenuPath)]
    public static void GenerateFromCSV()
    {
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
                                           autoComplete, savable, isAchievement);

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

        Debug.Log($"[QuestSOGenerator] 완료 — Quest {quests}, Achievement {achievements}, 건너뜀 {skipped}. " +
                  $"다음: RelicFairy/Gameplay/Quest/Rebuild Database 실행 → {kOutputRoot}");
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

        // 현재 Gold만 지원. Item은 ItemSO 룩업 필요 — 후속.
        if (rewardType.ToLowerInvariant() != "gold")
        {
            Debug.LogWarning($"[QuestSOGenerator] '{codeName}': rewardType '{rewardType}' 미지원 — 보상 없이 생성.");
            return null;
        }

        string path = $"{kOutputRoot}/Rewards/{Sanitize(codeName)}_Reward.asset";
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
                                     bool autoComplete, bool savable, bool isAchievement)
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
        return so;
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

    private static string Sanitize(string s)
    {
        foreach (char c in Path.GetInvalidFileNameChars())
            s = s.Replace(c, '_');
        return s.Replace(' ', '_');
    }
}
#endif
