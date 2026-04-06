using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// TileType[,] 그리드 + BlockPalette → 블록 인스턴스 생성.
/// 연출은 MapPresenter에 위임.
/// </summary>
public class MapBuilder
{
    /// <summary>생성된 블록 정보.</summary>
    public struct PlacedBlock
    {
        public GameObject instance;
        public Vector3 targetPosition;
        public float targetRotationY;
        public TileType tileType;
        public Vector2Int cell;
    }

    /// <summary>
    /// 그리드 기반으로 블록을 인스턴스화.
    /// </summary>
    public static List<PlacedBlock> Build(
        TileType[,] grid,
        BlockPalette palette,
        Transform parent,
        float cellSize = 1f,
        float baseY = 0f)
    {
        int w = grid.GetLength(0);
        int h = grid.GetLength(1);
        var result = new List<PlacedBlock>(w * h);
        // 맵 중앙이 (0, baseY, 0)에 오도록 오프셋 계산
        var offset = new Vector3((w - 1) * 0.5f * cellSize, 0f, (h - 1) * 0.5f * cellSize);
        var gridCenter = new Vector3(0f, baseY, 0f);

        for (int x = 0; x < w; x++)
        {
            for (int z = 0; z < h; z++)
            {
                var type = grid[x, z];
                if (type == TileType.Empty) continue;

                var blockDef = palette.Pick(type);
                if (blockDef == null)
                {
                    blockDef = palette.Pick(TileType.Floor);
                    if (blockDef == null) continue;
                }

                var targetPos = new Vector3(x * cellSize - offset.x, baseY, z * cellSize - offset.z);
                float rotY = CalcRotation(blockDef.facingRule, targetPos, gridCenter);

                var go = Object.Instantiate(blockDef.prefab, targetPos, Quaternion.Euler(0, rotY, 0), parent);
                go.name = $"Block_{x}_{z}_{type}";
                SetLayerRecursive(go, 3); // Ground layer (TagManager layer 3)

                result.Add(new PlacedBlock
                {
                    instance = go,
                    targetPosition = targetPos,
                    targetRotationY = rotY,
                    tileType = type,
                    cell = new Vector2Int(x, z),
                });
            }
        }

        return result;
    }

    /// <summary>그리드 크기에 맞는 투명 바닥 콜라이더 생성.</summary>
    public static GameObject CreateSafeFloor(int width, int height, float cellSize, float baseY, Transform parent)
    {
        var go = new GameObject("SafeFloor");
        go.layer = 3; // Ground (TagManager layer 3)
        go.transform.SetParent(parent, false);
        go.transform.position = new Vector3(0f, baseY - 0.05f, 0f);

        var col = go.AddComponent<BoxCollider>();
        col.size = new Vector3(width * cellSize + 2f, 0.1f, height * cellSize + 2f);

        return go;
    }

    private static float CalcRotation(FacingRule rule, Vector3 pos, Vector3 center)
    {
        switch (rule)
        {
            case FacingRule.FaceCenter:
                var dir = center - pos;
                if (dir.sqrMagnitude < 0.001f) return 0f;
                return Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg;

            case FacingRule.FaceOutward:
                var outDir = pos - center;
                if (outDir.sqrMagnitude < 0.001f) return 0f;
                return Mathf.Atan2(outDir.x, outDir.z) * Mathf.Rad2Deg;

            case FacingRule.Random:
                return Random.Range(0, 4) * 90f;

            default:
                return 0f;
        }
    }

    private static void SetLayerRecursive(GameObject go, int layer)
    {
        go.layer = layer;
        foreach (Transform child in go.transform)
            SetLayerRecursive(child.gameObject, layer);
    }
}
