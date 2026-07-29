#if UNITY_EDITOR
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>
/// QuestDatabase 에셋의 quests 리스트를 프로젝트의 Quest/Achievement SO로 자동 재구성.
/// 메뉴: Tools/Quest/Rebuild Database
/// </summary>
public static class QuestDatabaseAutoLinker
{
    private const string kMenuPath = "RelicFairy/Gameplay/Quest/Rebuild Database";

    [MenuItem(kMenuPath)]
    public static void RebuildDatabase()
    {
        // QuestDatabase 에셋 전체 조회
        string[] dbGuids = AssetDatabase.FindAssets("t:QuestDatabase");
        if (dbGuids.Length == 0)
        {
            Debug.LogError("[QuestDatabaseAutoLinker] QuestDatabase 에셋을 찾을 수 없습니다. 먼저 Generate From CSV 실행.");
            return;
        }

        // 프로젝트 내 Quest SO 전체 수집 (Achievement 제외)
        var questGuids = AssetDatabase.FindAssets("t:Quest");
        var allQuests = questGuids
            .Select(g => AssetDatabase.LoadAssetAtPath<Quest>(AssetDatabase.GUIDToAssetPath(g)))
            .Where(q => q != null && q.GetType() == typeof(Quest))
            .ToList();

        // 프로젝트 내 Achievement SO 전체 수집
        var achievementGuids = AssetDatabase.FindAssets("t:Achievement");
        var allAchievements = achievementGuids
            .Select(g => AssetDatabase.LoadAssetAtPath<Achievement>(AssetDatabase.GUIDToAssetPath(g)))
            .Where(a => a != null)
            .Cast<Quest>()
            .ToList();

        int rebuilt = 0;

        foreach (var dbGuid in dbGuids)
        {
            string dbPath = AssetDatabase.GUIDToAssetPath(dbGuid);
            var database = AssetDatabase.LoadAssetAtPath<QuestDatabase>(dbPath);
            if (database == null) continue;

            var serialized = new SerializedObject(database);
            var questsProp = serialized.FindProperty("quests");

            // 데이터베이스 이름으로 Quest/Achievement 구분
            bool isAchievementDb = dbPath.ToLower().Contains("achievement");
            var list = isAchievementDb ? allAchievements : allQuests;

            questsProp.ClearArray();
            for (int i = 0; i < list.Count; i++)
            {
                questsProp.InsertArrayElementAtIndex(i);
                questsProp.GetArrayElementAtIndex(i).objectReferenceValue = list[i];
            }

            serialized.ApplyModifiedProperties();
            EditorUtility.SetDirty(database);
            rebuilt++;

            Debug.Log($"[QuestDatabaseAutoLinker] {dbPath} → {list.Count}개 연결 완료 ({(isAchievementDb ? "Achievement" : "Quest")})");
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log($"[QuestDatabaseAutoLinker] {rebuilt}개 DB 재구성 완료 — Quest {allQuests.Count}, Achievement {allAchievements.Count}");
    }
}
#endif
