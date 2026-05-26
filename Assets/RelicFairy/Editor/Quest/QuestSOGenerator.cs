#if UNITY_EDITOR
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// CSV → Quest/Task ScriptableObject 일괄 생성 도구.
/// 메뉴: Tools/Quest/Generate From CSV
///
/// CSV 포맷 (헤더 필수):
/// questCode, questDisplay, taskCode, taskDisplay, category, targetString, needSuccess, autoComplete, savable
/// </summary>
public static class QuestSOGenerator
{
    private const string kMenuPath   = "Tools/Quest/Generate From CSV";
    private const string kOutputRoot = "Assets/RelicFairy/Data/Quest/Generated";

    [MenuItem(kMenuPath)]
    public static void GenerateFromCSV()
    {
        string csvPath = EditorUtility.OpenFilePanel("Quest CSV 선택", Application.dataPath, "csv");
        if (string.IsNullOrEmpty(csvPath))
        {
            Debug.Log("[QuestSOGenerator] 취소됨.");
            return;
        }

        string[] lines = File.ReadAllLines(csvPath, Encoding.UTF8);
        if (lines.Length < 2)
        {
            Debug.LogError("[QuestSOGenerator] CSV가 비어있거나 헤더만 있습니다.");
            return;
        }

        EnsureDirectory(kOutputRoot);
        EnsureDirectory($"{kOutputRoot}/Quests");
        EnsureDirectory($"{kOutputRoot}/Tasks");
        EnsureDirectory($"{kOutputRoot}/Targets");

        int created = 0;

        // 헤더 건너뜀
        for (int i = 1; i < lines.Length; i++)
        {
            string line = lines[i].Trim();
            if (string.IsNullOrEmpty(line)) continue;

            string[] cols = line.Split(',');
            if (cols.Length < 9)
            {
                Debug.LogWarning($"[QuestSOGenerator] Line {i + 1}: 컬럼 수 부족 — 건너뜀.");
                continue;
            }

            string questCode    = cols[0].Trim();
            // questDisplay   = cols[1]
            string taskCode     = cols[2].Trim();
            // taskDisplay    = cols[3]
            string categoryStr  = cols[4].Trim();
            string targetStr    = cols[5].Trim();
            int    needSuccess  = int.TryParse(cols[6].Trim(), out int ns) ? ns : 1;
            // autoComplete   = cols[7]
            // savable        = cols[8]

            // StringTarget SO
            string targetPath = $"{kOutputRoot}/Targets/Target_{taskCode}.asset";
            if (!File.Exists(Path.Combine(Application.dataPath, "../", targetPath)))
            {
                var targetSO = ScriptableObject.CreateInstance<StringTarget>();
                var serialized = new SerializedObject(targetSO);
                serialized.FindProperty("value").stringValue = targetStr;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                AssetDatabase.CreateAsset(targetSO, targetPath);
                created++;
            }

            // Task SO
            string taskPath = $"{kOutputRoot}/Tasks/{taskCode}.asset";
            if (!File.Exists(Path.Combine(Application.dataPath, "../", taskPath)))
            {
                var taskSO = ScriptableObject.CreateInstance<Task>();
                var serialized = new SerializedObject(taskSO);
                serialized.FindProperty("codeName").stringValue         = taskCode;
                serialized.FindProperty("description").stringValue      = cols[3].Trim();
                serialized.FindProperty("needSuccessToComplete").intValue = needSuccess;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                AssetDatabase.CreateAsset(taskSO, taskPath);
                created++;
            }
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log($"[QuestSOGenerator] 완료. 생성된 에셋: {created}개 → {kOutputRoot}");
        EditorUtility.DisplayDialog("Quest SO Generator", $"{created}개 에셋 생성 완료.\n경로: {kOutputRoot}", "확인");
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
}
#endif
