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
        int buffCount = 0;
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

                bool isBuffTile = type == TileType.BuffBox || type == TileType.BuffPedestal;

                // 버프 타일: 바닥 블록을 먼저 깔고 그 위에 버프 오브젝트 배치
                var renderType = isBuffTile ? TileType.Floor : type;
                var blockDef = palette.Pick(renderType);
                if (blockDef == null)
                {
                    blockDef = palette.Pick(TileType.Floor);
                    if (blockDef == null) continue;
                }

                var targetPos = new Vector3(x * cellSize - offset.x, baseY, z * cellSize - offset.z);
                float rotY = CalcRotation(blockDef.facingRule, targetPos, gridCenter);

                var go = Object.Instantiate(blockDef.prefab, targetPos, Quaternion.Euler(0, rotY, 0), parent);
                go.name = $"Block_{x}_{z}_{renderType}";
                SetLayerRecursive(go, 3); // Ground layer (TagManager layer 3)

                result.Add(new PlacedBlock
                {
                    instance = go,
                    targetPosition = targetPos,
                    targetRotationY = rotY,
                    tileType = renderType,
                    cell = new Vector2Int(x, z),
                });

                // 버프 타일: 전용 프리팹이 있으면 사용, 없으면 기본 트리거 오브젝트 생성
                if (isBuffTile)
                {
                    var buffDef = palette.Pick(type);
                    PlacedBlock buffBlock;

                    if (buffDef != null)
                    {
                        var buffGo = Object.Instantiate(buffDef.prefab, targetPos, Quaternion.identity, parent);
                        buffGo.name = $"Buff_{x}_{z}_{type}";
                        AttachBuffInteraction(buffGo, type);
                        buffBlock = new PlacedBlock
                        {
                            instance = buffGo,
                            targetPosition = targetPos,
                            targetRotationY = 0f,
                            tileType = type,
                            cell = new Vector2Int(x, z),
                        };
                        Debug.Log($"[MapBuilder] 버프 타일 배치 (프리팹): {type} at ({x},{z}) pos={targetPos}");
                        buffCount++;
                    }
                    else
                    {
                        buffBlock = CreateDefaultBuffObject(x, z, type, targetPos, cellSize, parent);
                        Debug.Log($"[MapBuilder] 버프 타일 배치 (임시큐브): {type} at ({x},{z}) pos={targetPos}");
                        buffCount++;
                    }

                    result.Add(buffBlock);
                }
            }
        }

        if (buffCount > 0)
            Debug.Log($"[MapBuilder] 맵 빌드 완료: 총 블록 {result.Count}개, 버프 타일 {buffCount}개");

        return result;
    }

    /// <summary>전용 프리팹이 없을 때 기본 버프 오브젝트 생성.</summary>
    private static PlacedBlock CreateDefaultBuffObject(
        int x, int z, TileType type, Vector3 pos, float cellSize, Transform parent)
    {
        bool isPedestal = type == TileType.BuffPedestal;

        var go = new GameObject($"Buff_{x}_{z}_{type}");
        go.transform.SetParent(parent, false);
        go.transform.position = pos + Vector3.up * (isPedestal ? 0.05f : 0.5f);

        // 트리거 콜라이더
        var col = go.AddComponent<BoxCollider>();
        col.isTrigger = true;
        col.size = isPedestal
            ? new Vector3(cellSize * 0.8f, 0.3f, cellSize * 0.8f)
            : new Vector3(cellSize * 0.5f, cellSize * 0.8f, cellSize * 0.5f);

        // 시각 표시용 큐브 (임시 — 추후 프리팹으로 교체)
        var visual = GameObject.CreatePrimitive(PrimitiveType.Cube);
        visual.transform.SetParent(go.transform, false);
        visual.transform.localScale = isPedestal
            ? new Vector3(cellSize * 0.7f, 0.1f, cellSize * 0.7f)
            : new Vector3(cellSize * 0.4f, cellSize * 0.4f, cellSize * 0.4f);

        // 콜라이더 제거 (시각용만)
        var visualCol = visual.GetComponent<Collider>();
        if (visualCol != null) Object.Destroy(visualCol);

        // 색상: 발판=파랑, 상자=노랑
        var renderer = visual.GetComponent<Renderer>();
        if (renderer != null)
            renderer.material.color = isPedestal
                ? new Color(0.3f, 0.5f, 1f, 0.8f)
                : new Color(1f, 0.85f, 0.2f, 0.8f);

        AttachBuffInteraction(go, type);

        return new PlacedBlock
        {
            instance = go,
            targetPosition = go.transform.position,
            targetRotationY = 0f,
            tileType = type,
            cell = new Vector2Int(x, z),
        };
    }

    private static void AttachBuffInteraction(GameObject go, TileType type)
    {
        var interaction = go.GetComponent<BuffTileInteraction>();
        if (interaction == null)
            interaction = go.AddComponent<BuffTileInteraction>();

        interaction.Setup(type == TileType.BuffPedestal);

        // 트리거 콜라이더 보장
        var col = go.GetComponent<Collider>();
        if (col != null) col.isTrigger = true;
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
