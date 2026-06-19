using UnityEngine;
using UnityEditor;

/// <summary>
/// 씬 내 Missing Script(깨진 MonoBehaviour 참조) 진단/정리 유틸.
/// 일회성 진단 목적 — 정리 후 삭제 가능.
/// </summary>
public static class MissingScriptUtil
{
    [MenuItem("Tools/Missing Scripts/Find In Scene")]
    public static void Find()
    {
        var all = Object.FindObjectsByType<GameObject>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        int goCount = 0;
        int compCount = 0;
        foreach (var go in all)
        {
            int missing = GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(go);
            if (missing > 0)
            {
                goCount++;
                compCount += missing;
                Debug.Log($"[MissingScript] {missing}x @ {GetPath(go)}", go);
            }
        }
        Debug.Log($"[MissingScript] DONE — {compCount} missing components on {goCount} GameObjects");
    }

    [MenuItem("Tools/Missing Scripts/Remove In Scene")]
    public static void Remove()
    {
        var all = Object.FindObjectsByType<GameObject>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        int removed = 0;
        foreach (var go in all)
        {
            int n = GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(go);
            if (n > 0)
            {
                GameObjectUtility.RemoveMonoBehavioursWithMissingScript(go);
                removed += n;
            }
        }
        Debug.Log($"[MissingScript] REMOVED {removed} missing components");
    }

    // 씬 전체의 벽 타입 오브젝트를 Wall(8) 레이어로(구역 제한 없음).
    // 카메라 오클루전 페이드 대상이 되도록(콜라이더 있는 벽만).
    [MenuItem("Tools/Missing Scripts/Set ALL Walls To Wall Layer")]
    public static void SetAllWallsLayer()
    {
        int wall = LayerMask.NameToLayer("Wall");
        if (wall < 0) { Debug.LogError("[WallLayer] 'Wall' layer not found"); return; }

        var all = Object.FindObjectsByType<GameObject>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        int changed = 0;
        foreach (var go in all)
        {
            string n = go.name;
            bool wallType = n.StartsWith("SM_ArchWall") || n.StartsWith("SM_TrimConcrete")
                         || n.StartsWith("SM_StoneArch") || n.StartsWith("SM_Arche")
                         || n.StartsWith("SM_Pillar")    || n.StartsWith("SM_Column")
                         || n.StartsWith("Wall_");
            if (!wallType) continue;
            if (go.GetComponent<Collider>() == null) continue; // 콜라이더 있는 것만(오클루전 캐스트 대상)
            if (go.layer != wall) { go.layer = wall; changed++; }
        }
        Debug.Log($"[WallLayer] DONE — set {changed} objects to Wall layer ({wall})");
    }

    private static string GetPath(GameObject go)
    {
        var path = go.name;
        var t = go.transform;
        while (t.parent != null) { t = t.parent; path = t.name + "/" + path; }
        return path;
    }
}
