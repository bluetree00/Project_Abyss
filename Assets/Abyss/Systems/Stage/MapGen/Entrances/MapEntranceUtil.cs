using UnityEngine;

/// <summary>IMapEntrance 구현체들이 공유하는 유틸.</summary>
internal static class MapEntranceUtil
{
    public static void SetCollidersEnabled(GameObject go, bool enabled)
    {
        if (go == null) return;
        foreach (var col in go.GetComponentsInChildren<Collider>(true))
            col.enabled = enabled;
    }
}
