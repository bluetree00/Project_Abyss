using UnityEngine;

/// <summary>
/// 두 존 사이의 갭에 코리더 타일을 깔고 좌우 가장자리에 장식 오브젝트를 배치한다.
/// GameRunBootstrapper가 인접 존 쌍을 스폰한 뒤 호출한다.
/// </summary>
public static class CorridorBridgeSpawner
{
    /// <param name="fromZone">출발 존 레이아웃 데이터</param>
    /// <param name="toZone">도착 존 레이아웃 데이터</param>
    /// <param name="fromCenter">출발 존 월드 중심</param>
    /// <param name="toCenter">도착 존 월드 중심</param>
    /// <param name="style">테마별 코리더 스타일 (null이면 스폰 생략)</param>
    /// <param name="parent">생성된 오브젝트의 부모 트랜스폼</param>
    /// <param name="blockCellSize">블록 한 칸의 월드 크기 (일반적으로 1f)</param>
    public static void Spawn(
        ZoneLayoutEntry fromZone, ZoneLayoutEntry toZone,
        Vector3 fromCenter, Vector3 toCenter,
        CorridorStyleSO style, Transform parent, float blockCellSize)
    {
        if (style?.floorTilePrefab == null) return;

        var fromDoor = CalcDoorPos(fromZone, fromCenter, toCenter, blockCellSize);
        var toDoor   = CalcDoorPos(toZone, toCenter, fromCenter, blockCellSize);

        var dir   = (toDoor - fromDoor).normalized;
        var right = Vector3.Cross(Vector3.up, dir).normalized;
        float gap  = Vector3.Distance(fromDoor, toDoor);
        int count  = Mathf.CeilToInt(gap / blockCellSize);

        for (int i = 0; i < count; i++)
        {
            var pos = fromDoor + dir * ((i + 0.5f) * blockCellSize);
            Object.Instantiate(style.floorTilePrefab, pos, Quaternion.LookRotation(dir), parent);
        }

        SpawnEdge(fromDoor, toDoor, right, style.leftEdgePrefabs, style.edgeObjectSpacing, blockCellSize, parent);
        SpawnEdge(fromDoor, toDoor, -right, style.rightEdgePrefabs, style.edgeObjectSpacing, blockCellSize, parent);

        if (style.hasVoidBelow && style.voidFogPrefab != null)
        {
            var mid = Vector3.Lerp(fromDoor, toDoor, 0.5f);
            Object.Instantiate(style.voidFogPrefab, mid, Quaternion.identity, parent);
        }
    }

    // 존 경계면의 문 중심 좌표를 계산한다.
    private static Vector3 CalcDoorPos(
        ZoneLayoutEntry zone, Vector3 zoneCenter, Vector3 neighborCenter, float blockCellSize)
    {
        var dir   = (neighborCenter - zoneCenter).normalized;
        float halfW = zone.grid_width  * blockCellSize * 0.5f;
        float halfH = zone.grid_height * blockCellSize * 0.5f;

        if (Mathf.Abs(dir.z) >= Mathf.Abs(dir.x))
            return zoneCenter + new Vector3(0f, 0f, Mathf.Sign(dir.z) * halfH);
        else
            return zoneCenter + new Vector3(Mathf.Sign(dir.x) * halfW, 0f, 0f);
    }

    private static void SpawnEdge(
        Vector3 start, Vector3 end, Vector3 side,
        GameObject[] prefabs, float spacing, float blockCellSize, Transform parent)
    {
        if (prefabs == null || prefabs.Length == 0) return;

        var dir    = (end - start).normalized;
        float len  = Vector3.Distance(start, end);
        float offset = blockCellSize * 1.5f;

        for (float d = spacing * 0.5f; d < len; d += spacing)
        {
            var pick = prefabs[Random.Range(0, prefabs.Length)];
            if (pick == null) continue;
            var pos = start + dir * d + side * offset;
            Object.Instantiate(pick, pos, Quaternion.LookRotation(dir), parent);
        }
    }
}
