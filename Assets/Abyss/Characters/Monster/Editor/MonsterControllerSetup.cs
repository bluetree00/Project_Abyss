#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

/// <summary>
/// MonsterBase.controller를 복제하여 각 몬스터별 Controller를 생성하고
/// 상태별 클립을 자동 할당하는 에디터 유틸리티.
/// 메뉴: Tools > Monster > Setup All Controllers
/// </summary>
public static class MonsterControllerSetup
{
    private const string BaseControllerPath =
        "Assets/Abyss/Characters/Monster/Monster/Core/Animator/MonsterBase.controller";

    // 공용 상태 이름 (Base Controller 기준)
    private static readonly string[] StateNames =
        { "Idle", "Walk", "Run", "AttackReady", "Attack", "GetHit", "Die" };

    // 상태별 클립 검색 키워드 (우선순위 순)
    private static readonly Dictionary<string, string[]> ClipKeywords = new()
    {
        ["Idle"]        = new[] { "IdleNormal", "Idle_Normal", "IdlePlant" },
        ["Walk"]        = new[] { "WalkFWD", "walkFWD", "Walk", "FlyFWD" },
        ["Run"]         = new[] { "RunFWD", "runFWD", "Run", "FlyFWDFast" },
        ["AttackReady"] = new[] { "IdleBattle" },
        ["Attack"]      = new[] { "Attack01" },
        ["GetHit"]      = new[] { "GetHit" },
        ["Die"]         = new[] { "Die" },
    };

    [MenuItem("Tools/Monster/Setup All Controllers")]
    public static void SetupAll()
    {
        var baseCtrl = AssetDatabase.LoadMainAssetAtPath(BaseControllerPath) as AnimatorController;
        if (baseCtrl == null)
        {
            Debug.LogError($"Base Controller not found: {BaseControllerPath}");
            return;
        }

        // 모든 몬스터 폴더 탐색
        string monstersRoot = "Assets/Abyss/Characters/Monster/Monster";
        var monsterFolders = Directory.GetDirectories(monstersRoot)
            .Where(d => !Path.GetFileName(d).StartsWith("Core") &&
                        !Path.GetFileName(d).StartsWith("Boss") &&
                        !Path.GetFileName(d).StartsWith("States") &&
                        !Path.GetFileName(d).StartsWith("Editor") &&
                        Directory.Exists(Path.Combine(d, "Prefab")))
            .ToList();

        int created = 0;
        foreach (string folder in monsterFolders)
        {
            string monsterName = Path.GetFileName(folder);
            string ctrlDir = Path.Combine(folder, "Animator");
            string ctrlPath = Path.Combine(ctrlDir, $"{monsterName}.controller").Replace('\\', '/');

            // 이미 존재하면 스킵
            if (File.Exists(ctrlPath))
            {
                Debug.Log($"[Skip] {monsterName}: Controller already exists");
                continue;
            }

            // 디렉토리 생성
            if (!Directory.Exists(ctrlDir))
                Directory.CreateDirectory(ctrlDir);

            // Base Controller 복제
            AssetDatabase.CopyAsset(BaseControllerPath, ctrlPath);
            var ctrl = AssetDatabase.LoadAssetAtPath<AnimatorController>(ctrlPath);
            if (ctrl == null)
            {
                Debug.LogError($"Failed to copy controller for {monsterName}");
                continue;
            }

            // 해당 몬스터의 애니메이션 클립 검색
            var clips = FindMonsterClips(monsterName);

            // Unity 6: SerializedObject 경유로 상태 클립 할당
            var so = new SerializedObject(ctrl);
            var layersProp = so.FindProperty("m_AnimatorLayers");
            if (layersProp != null && layersProp.arraySize > 0)
            {
                var smRef = layersProp.GetArrayElementAtIndex(0).FindPropertyRelative("m_StateMachine");
                var sm = smRef.objectReferenceValue as AnimatorStateMachine;
                if (sm != null)
                {
                    foreach (var stateInfo in sm.states)
                    {
                        string stateName = stateInfo.state.name;
                        if (clips.TryGetValue(stateName, out var clip))
                        {
                            stateInfo.state.motion = clip;
                            EditorUtility.SetDirty(stateInfo.state);
                        }
                    }
                }
            }
            so.ApplyModifiedPropertiesWithoutUndo();

            EditorUtility.SetDirty(ctrl);
            created++;
            Debug.Log($"[Created] {monsterName}: {clips.Count}/7 clips assigned");
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log($"Done! Created {created} controllers");
    }

    private static Dictionary<string, AnimationClip> FindMonsterClips(string monsterName)
    {
        var result = new Dictionary<string, AnimationClip>();

        // 1차: 서드파티 + 자체 애니메이션 폴더에서 검색
        string[] searchPaths =
        {
            "Assets/_ThirdParty/RPGMonsterBundlePolyart",
            "Assets/Abyss/Animations",
            "Assets/Abyss/Characters/Monster/Monster",
        };

        // 몬스터 이름 변형 (Controller 이름과 폴더 이름이 다를 수 있음)
        var nameVariants = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase)
        {
            monsterName,
            monsterName.Replace("Monster", ""),
        };
        // 특수 매핑
        if (monsterName == "Snail") nameVariants.Add("TurtleShell");
        if (monsterName == "NagaWizard") { nameVariants.Add("NagarWizard"); nameVariants.Add("Nagar"); }
        if (monsterName == "MushroomAngry") nameVariants.Add("Mushroom");
        if (monsterName == "MushroomSmile") nameVariants.Add("Mushroom");

        // FBX에서 AnimationClip 찾기
        var allClips = new List<AnimationClip>();
        foreach (string searchPath in searchPaths)
        {
            string[] guids = AssetDatabase.FindAssets("t:AnimationClip", new[] { searchPath });
            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                // 경로에 몬스터 이름이 포함되어야 함
                bool match = nameVariants.Any(n => path.IndexOf(n, System.StringComparison.OrdinalIgnoreCase) >= 0);
                if (!match) continue;

                var assets = AssetDatabase.LoadAllAssetsAtPath(path);
                foreach (var asset in assets)
                {
                    if (asset is AnimationClip clip && !clip.name.StartsWith("__preview__"))
                        allClips.Add(clip);
                }
            }
        }

        // 각 상태에 맞는 클립 매칭
        foreach (string stateName in StateNames)
        {
            if (!ClipKeywords.TryGetValue(stateName, out var keywords))
                continue;

            AnimationClip bestMatch = null;

            // 몬스터 이름 접두사가 있는 클립 우선
            foreach (string keyword in keywords)
            {
                // {MonsterName}_{Keyword} 형태 우선
                foreach (var variant in nameVariants)
                {
                    bestMatch = allClips.FirstOrDefault(c =>
                        c.name.Equals($"{variant}_{keyword}", System.StringComparison.OrdinalIgnoreCase));
                    if (bestMatch != null) break;
                }
                if (bestMatch != null) break;

                // {Keyword}_{MonsterName}_Anim 형태
                foreach (var variant in nameVariants)
                {
                    bestMatch = allClips.FirstOrDefault(c =>
                        c.name.IndexOf(keyword, System.StringComparison.OrdinalIgnoreCase) >= 0 &&
                        c.name.IndexOf(variant, System.StringComparison.OrdinalIgnoreCase) >= 0);
                    if (bestMatch != null) break;
                }
                if (bestMatch != null) break;

                // 키워드만으로 매칭 (접두사 없음)
                bestMatch = allClips.FirstOrDefault(c =>
                    c.name.Equals(keyword, System.StringComparison.OrdinalIgnoreCase));
                if (bestMatch != null) break;
            }

            if (bestMatch != null)
                result[stateName] = bestMatch;
        }

        // Run이 없으면 Walk 사용
        if (!result.ContainsKey("Run") && result.ContainsKey("Walk"))
            result["Run"] = result["Walk"];

        // AttackReady가 없으면 Idle 사용
        if (!result.ContainsKey("AttackReady") && result.ContainsKey("Idle"))
            result["AttackReady"] = result["Idle"];

        return result;
    }
}
#endif
