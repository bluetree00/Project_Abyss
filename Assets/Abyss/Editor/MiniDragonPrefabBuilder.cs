#if UNITY_EDITOR
using Abyss.Monster;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// 미니드래곤 프리팹 생성 유틸리티.
/// Abyss > Tools > Build MiniDragon Prefab 실행.
/// </summary>
public static class MiniDragonPrefabBuilder
{
    private const string DragonModelPath =
        "Assets/_ThirdParty/RPGMonsterBundlePolyart/CommonStuffs/Prefab/Wave01/CharacterMaskTint/DragonPAMaskTint.prefab";

    private const string OutputPath =
        "Assets/Abyss/Characters/Monster/Monster/DragonBoss/Prefab/MiniDragon.prefab";

    [MenuItem("Abyss/Tools/Build MiniDragon Prefab")]
    public static void Build()
    {
        // 드래곤 모델 프리팹 로드
        var dragonModel = AssetDatabase.LoadAssetAtPath<GameObject>(DragonModelPath);
        if (dragonModel == null)
        {
            Debug.LogError($"[MiniDragonPrefabBuilder] DragonPAMaskTint.prefab을 찾을 수 없습니다.\n경로: {DragonModelPath}");
            return;
        }

        // 루트 오브젝트 생성
        var root = new GameObject("MiniDragon");
        root.transform.localScale = Vector3.one;

        // NavMeshAgent
        var agent = root.AddComponent<NavMeshAgent>();
        agent.radius = 0.5f;
        agent.height = 1.5f;
        agent.speed  = 4f;
        agent.stoppingDistance = 5f;

        // CapsuleCollider
        var col = root.AddComponent<CapsuleCollider>();
        col.center = new Vector3(0f, 0.75f, 0f);
        col.radius = 0.5f;
        col.height = 1.5f;

        // MiniDragonController
        root.AddComponent<MiniDragonController>();

        // 드래곤 모델 자식으로 인스턴스화
        var modelGo = (GameObject)PrefabUtility.InstantiatePrefab(dragonModel, root.transform);
        modelGo.transform.localPosition = Vector3.zero;
        modelGo.transform.localRotation = Quaternion.identity;
        modelGo.transform.localScale    = Vector3.one * 0.5f; // 보스의 절반 크기

        // 프리팹 저장
        bool success;
        PrefabUtility.SaveAsPrefabAsset(root, OutputPath, out success);
        Object.DestroyImmediate(root);

        if (success)
            Debug.Log($"[MiniDragonPrefabBuilder] MiniDragon.prefab 생성 완료 → {OutputPath}");
        else
            Debug.LogError("[MiniDragonPrefabBuilder] 프리팹 저장 실패");

        AssetDatabase.Refresh();
    }
}
#endif
