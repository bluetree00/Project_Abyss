#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.AI;

public static class NavMeshBaker
{
    [MenuItem("Tools/Bake NavMesh Now")]
    public static void Bake()
    {
        NavMeshBuilder.BuildNavMesh();
    }
}
#endif