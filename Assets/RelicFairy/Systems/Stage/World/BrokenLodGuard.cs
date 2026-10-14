using UnityEngine;

/// <summary>
/// 하위 LOD가 깨진 에셋 메시를 LOD0에 고정한다(10-12 · 83 Ch3 점검 D11).
/// 벽 촛대(SM_WallCandleabra_01a) FBX의 LOD1~4는 본체에서 9~24 m 떨어진 정점 3개를 쓰는 면이 있어, 켜지면 깨진 삼각형이 방을 가로지르고
/// 경계가 33 m로 커져 컬링도 안 된다. 손맵 아레나(Arena_Boss_Ch3 · Road_Boss_Ch3 · 인트로 변형)는 이 메시를 프리팹 연결 없이
/// 18곳에 풀어 담아 원본 프리팹을 고쳐도 전파되지 않는다 → 띄운 직후 해당 LODGroup만 고정한다(FBX · 프리팹은 그대로).
/// </summary>
public static class BrokenLodGuard
{
    private static readonly string[] BrokenMeshes = { "SM_WallCandleabra_01a" };

    /// <summary><paramref name="root"/> 아래에서 깨진 메시를 쓰는 LODGroup을 LOD0에 고정하고 그 수를 돌려준다.</summary>
    public static int Apply(GameObject root)
    {
        if (root == null) return 0;
        int n = 0;
        foreach (var g in root.GetComponentsInChildren<LODGroup>(true))
        {
            var lods = g.GetLODs();
            if (lods.Length < 2 || !UsesBrokenMesh(lods[0])) continue;
            g.ForceLOD(0);
            n++;
        }
        if (n > 0) Debug.Log($"[BrokenLodGuard] {root.name} — 깨진 하위 LOD {n}곳 LOD0 고정");
        return n;
    }

    private static bool UsesBrokenMesh(LOD lod)
    {
        foreach (var r in lod.renderers)
        {
            if (r == null || !r.TryGetComponent<MeshFilter>(out var mf) || mf.sharedMesh == null) continue;
            foreach (var b in BrokenMeshes)
                if (mf.sharedMesh.name.StartsWith(b)) return true;
        }
        return false;
    }
}
