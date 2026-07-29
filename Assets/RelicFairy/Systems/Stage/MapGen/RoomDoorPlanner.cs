using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 격리형 방의 문 계획. MapDataLoader가 추출한 doorInfos를 캐논 엣지 기준으로
/// 입구(South)/직진(North)/턴(East·West)으로 분류하고, 선택된 문을 grid에 개방한다.
///
/// 캐논 로컬 규약: 입구=아래(South) / 직진=위(North) / 턴=옆(East·West).
/// 회전·미러는 GridTransform이 grid_csv 문자열에 적용한 뒤이므로, 여기서는 변환된
/// 그리드 기준으로 엣지를 그대로 신뢰한다.
/// </summary>
public static class RoomDoorPlanner
{
    /// <summary>엣지별로 분류된 문 앵커.</summary>
    public struct Classified
    {
        public Vector2Int?      entrance; // South 입구 (없으면 시작방)
        public Vector2Int?      forward;  // North 직진 출구
        public List<Vector2Int> turns;    // East/West 턴 출구 후보
    }

    /// <summary>
    /// 헤딩(진행방향, 0=N/1=E/2=S/3=W 쿼터턴) 기준으로 분류한다.
    /// 방이 heading만큼 회전 빌드된 상태이므로, 절대 엣지로 역할을 판정한다:
    /// 입구=뒤(heading+2) / 직진=앞(heading) / 턴=좌우(heading±1).
    /// (DoorEdge 순서 North=0,East=1,South=2,West=3 가 시계방향과 일치)
    /// </summary>
    public static Classified ClassifyWithHeading(IReadOnlyDictionary<Vector2Int, DoorInfo> doorInfos, int heading)
    {
        var c = new Classified { turns = new List<Vector2Int>() };
        if (doorInfos == null) return c;

        int h = ((heading % 4) + 4) % 4;
        int entranceEdge = ((int)DoorEdge.South + h) % 4; // 뒤
        int forwardEdge  = ((int)DoorEdge.North + h) % 4; // 앞(헤딩)

        foreach (var kv in doorInfos)
        {
            int e = (int)kv.Value.edge;
            if      (e == entranceEdge && !c.entrance.HasValue) c.entrance = kv.Key;
            else if (e == forwardEdge  && !c.forward.HasValue)  c.forward  = kv.Key;
            else                                                c.turns.Add(kv.Key);
        }
        return c;
    }

    /// <summary>doorInfos를 캐논 엣지 기준으로 분류 (heading=0과 동일). 각 역할은 첫 번째 앵커 채택.</summary>
    public static Classified Classify(IReadOnlyDictionary<Vector2Int, DoorInfo> doorInfos)
    {
        var c = new Classified { turns = new List<Vector2Int>() };
        if (doorInfos == null) return c;

        foreach (var kv in doorInfos)
        {
            switch (kv.Value.edge)
            {
                case DoorEdge.South: if (!c.entrance.HasValue) c.entrance = kv.Key; break;
                case DoorEdge.North: if (!c.forward.HasValue)  c.forward  = kv.Key; break;
                case DoorEdge.East:
                case DoorEdge.West:  c.turns.Add(kv.Key); break;
            }
        }
        return c;
    }

    /// <summary>문 1개를 grid에 개방 — 엣지를 따라 width 셀을 Floor로 치환(앵커 중심 ±half).</summary>
    public static void Open(TileType[,] grid, Vector2Int cell, DoorInfo info)
    {
        if (grid == null) return;
        int w    = grid.GetLength(0);
        int h    = grid.GetLength(1);
        int half = Mathf.Max(0, info.width / 2);

        switch (info.edge)
        {
            case DoorEdge.North:
            case DoorEdge.South:
            {
                int z = info.edge == DoorEdge.North ? h - 1 : 0;
                for (int x = cell.x - half; x <= cell.x + half; x++)
                    if (x >= 0 && x < w) grid[x, z] = TileType.Floor;
                break;
            }
            case DoorEdge.East:
            case DoorEdge.West:
            {
                int x = info.edge == DoorEdge.East ? w - 1 : 0;
                for (int z = cell.y - half; z <= cell.y + half; z++)
                    if (z >= 0 && z < h) grid[x, z] = TileType.Floor;
                break;
            }
        }
    }
}
