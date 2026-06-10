#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

/// <summary>
/// @UIRoot 프리팹의 missing script를 제거하고 BossBark를 설치하는 툴.
/// 메뉴: RelicFairy/UI/Fix @UIRoot Missing Scripts and Setup BossBark
/// </summary>
public static class FixPrefabMissingScripts
{
    private const string PrefabPath = "Assets/RelicFairy/UI/RootUI/@UIRoot.prefab";

    [MenuItem("RelicFairy/UI/Fix @UIRoot Missing Scripts")]
    public static void Execute()
    {
        using var scope = new PrefabUtility.EditPrefabContentsScope(PrefabPath);
        var root = scope.prefabContentsRoot;

        int removedTotal = RemoveMissingScriptsDeep(root);
        Debug.Log($"[FixPrefab] missing script 제거 완료: {removedTotal}개");

        // BossBark 설치 (같은 scope 안에서 처리)
        CreateBossBarkSetup.SetupInRoot(root);
    }

    private static int RemoveMissingScriptsDeep(GameObject go)
    {
        int count = GameObjectUtility.RemoveMonoBehavioursWithMissingScript(go);
        for (int i = 0; i < go.transform.childCount; i++)
            count += RemoveMissingScriptsDeep(go.transform.GetChild(i).gameObject);
        return count;
    }
}
#endif
