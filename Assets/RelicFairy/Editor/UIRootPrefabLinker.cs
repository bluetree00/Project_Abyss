#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

public static class UIRootPrefabLinker
{
    const string k_UIRootPath       = "Assets/RelicFairy/UI/RootUI/@UIRoot.prefab";
    const string k_ScenePrefabsDir  = "Assets/RelicFairy/UI/Scene/ScenePrefabs";
    const string k_SceneChildPath   = "Canvas_Scene/@Scene";

    [MenuItem("Tools/RelicFairy/Link @UIRoot Scene Prefabs")]
    public static void LinkScenePrefabs()
    {
        if (!File.Exists(k_UIRootPath))
        {
            Debug.LogError($"[UIRootLinker] @UIRoot.prefab not found at {k_UIRootPath}");
            return;
        }

        using var scope = new PrefabUtility.EditPrefabContentsScope(k_UIRootPath);
        var root = scope.prefabContentsRoot;

        var sceneRoot = root.transform.Find(k_SceneChildPath);
        if (sceneRoot == null)
        {
            Debug.LogError("[UIRootLinker] Canvas_Scene/@Scene not found inside @UIRoot");
            return;
        }

        // 1) 처리할 대상 수집 (수정 중 반복 불가이므로 먼저 스냅샷)
        var targets = new List<(string name, int siblingIdx, bool active)>();
        for (int i = 0; i < sceneRoot.childCount; i++)
        {
            var child = sceneRoot.GetChild(i);
            string candidatePath = $"{k_ScenePrefabsDir}/{child.name}.prefab";
            if (File.Exists(candidatePath))
                targets.Add((child.name, i, child.gameObject.activeSelf));
            else
                Debug.Log($"[UIRootLinker] '{child.name}' 에 대응하는 프리팹 없음 — 스킵");
        }

        if (targets.Count == 0)
        {
            Debug.Log("[UIRootLinker] 링크할 대상 없음.");
            return;
        }

        // 2) 각 대상: 현재 @UIRoot 상태를 프리팹 파일로 저장 → 제거 → 네스티드 인스턴스로 교체
        foreach (var (name, siblingIdx, wasActive) in targets)
        {
            var child = sceneRoot.Find(name);
            if (child == null)
            {
                Debug.LogWarning($"[UIRootLinker] '{name}' 을 @Scene 에서 찾을 수 없음 — 스킵");
                continue;
            }

            string prefabPath = $"{k_ScenePrefabsDir}/{name}.prefab";

            // @UIRoot 안의 현재 상태를 임시 복사해 독립 프리팹으로 저장
            var tempGO = Object.Instantiate(child.gameObject);
            tempGO.name = name;
            var savedAsset = PrefabUtility.SaveAsPrefabAsset(tempGO, prefabPath);
            Object.DestroyImmediate(tempGO);

            if (savedAsset == null)
            {
                Debug.LogError($"[UIRootLinker] '{name}' 저장 실패 ({prefabPath})");
                continue;
            }

            // 기존 복사본 제거
            Object.DestroyImmediate(child.gameObject);

            // 네스티드 프리팹 인스턴스로 교체
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(savedAsset, sceneRoot);
            instance.transform.SetSiblingIndex(siblingIdx);
            instance.SetActive(wasActive);

            Debug.Log($"[UIRootLinker] 완료: '{name}' → nested prefab 인스턴스");
        }

        AssetDatabase.SaveAssets();
        Debug.Log("[UIRootLinker] 모든 Scene UI 프리팹 링크 완료!");
    }
}
#endif
