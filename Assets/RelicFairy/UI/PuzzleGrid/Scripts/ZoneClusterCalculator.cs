using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 그리드 내 동일 존(zone) 셀의 최대 연결 클러스터 크기를 BFS로 계산한다.
/// 4방향 인접(상하좌우) 기준. absCol 좌표계와 동일하게 Vector2Int(x=absCol, y=hexRow) 사용.
/// </summary>
public static class ZoneClusterCalculator
{
    private static readonly Vector2Int[] s_Neighbors =
    {
        new(-1, 0), new(1, 0), new(0, -1), new(0, 1),
    };

    /// <summary>
    /// occupied: 점유된 셀 좌표 집합 (Vector2Int(absCol, hexRow))
    /// zones:    좌표 → 존 코드 맵 ('A','D','H','S','M','L','+')
    /// 반환값:   zoneId → 해당 존의 최대 연결 클러스터 크기
    /// </summary>
    public static Dictionary<string, int> Compute(
        HashSet<Vector2Int> occupied,
        Dictionary<Vector2Int, char> zones)
    {
        var result  = new Dictionary<string, int>();
        var visited = new HashSet<Vector2Int>();
        var queue   = new Queue<Vector2Int>();

        if (occupied == null || occupied.Count == 0)
            return result;

        foreach (var start in occupied)
        {
            if (visited.Contains(start)) continue;
            if (!zones.TryGetValue(start, out char zoneCode)) continue;

            string zoneId = ElementDef.CodeToId(zoneCode);
            if (zoneId == null) continue;

            queue.Clear();
            queue.Enqueue(start);
            visited.Add(start);

            int size = 0;
            while (queue.Count > 0)
            {
                var pos = queue.Dequeue();
                size++;

                foreach (var offset in s_Neighbors)
                {
                    var nb = pos + offset;
                    if (visited.Contains(nb)) continue;
                    if (!occupied.Contains(nb)) continue;
                    if (!zones.TryGetValue(nb, out char nc) || nc != zoneCode) continue;

                    visited.Add(nb);
                    queue.Enqueue(nb);
                }
            }

            result.TryGetValue(zoneId, out int prev);
            if (size > prev) result[zoneId] = size;
        }

        return result;
    }
}
