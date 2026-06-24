using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// grid_csv 문자열을 다시 파싱해 각 셀의 토큰을 TokenRegistry로 조회하고 Execute한다.
/// MapDataLoader와 동일한 좌표계(좌하단=0,0, 첫 행=최상단)를 사용한다.
///
/// MapDataLoader.Parse()는 구조 그리드(TileType[,])를 MapBuilder에 제공하고,
/// TokenParser.Execute()는 그 위에 기능 오브젝트(장식·구조물 등)를 배치한다.
/// </summary>
public static class TokenParser
{
    /// <summary>
    /// grid_csv 전체를 순회해 지정 phase에 등록된 핸들러가 있는 토큰만 Execute한다.
    /// </summary>
    /// <param name="gridCsv">MapRoomEntry.grid_csv 또는 ZoneLayoutEntry.grid_csv</param>
    /// <param name="gridWidth">grid.GetLength(0) — X축 셀 수</param>
    /// <param name="gridHeight">grid.GetLength(1) — Z축 셀 수</param>
    /// <param name="baseCtx">공유 데이터가 채워진 컨텍스트. RawToken/Cell/WorldPos는 이 메서드가 덮어씀.</param>
    /// <param name="phase">실행할 페이즈. 해당 phase인 핸들러만 실행됨.</param>
    public static void Execute(
        string gridCsv,
        int gridWidth,
        int gridHeight,
        TokenContext baseCtx,
        TokenPhase phase = TokenPhase.PostBuild)
    {
        if (string.IsNullOrWhiteSpace(gridCsv) || baseCtx?.Parent == null) return;

        TokenRegistry.EnsureInitialized();

        var rows   = SplitRows(gridCsv);
        int h      = rows.Length;
        float cs   = baseCtx.CellSize;
        float baseY = baseCtx.BaseY;

        // 맵 중앙이 parent.position에 오도록 — MapBuilder와 동일한 오프셋
        var offset = new Vector3((gridWidth - 1) * 0.5f * cs, 0f, (gridHeight - 1) * 0.5f * cs);

        // 방 내부 필드 경계(걷기셀 월드 AABB)를 1회 계산 — 스포너가 게이트/복도로 새지 않게 제한.
        if (baseCtx.FieldBounds == null && baseCtx.Grid != null)
            baseCtx.FieldBounds = ComputeFieldBounds(baseCtx.Grid, gridWidth, gridHeight, cs, baseY, baseCtx.Parent, offset);

        for (int z = 0; z < h; z++)
        {
            var cells = rows[z].Split(',');
            int rowZ  = h - 1 - z; // csv 첫 줄 = 맨 윗줄 → z 반전

            for (int x = 0; x < cells.Length && x < gridWidth; x++)
            {
                string raw = cells[x].Trim();
                if (string.IsNullOrEmpty(raw) || raw == ".") continue;

                var handler = TokenRegistry.Resolve(raw, phase);
                if (handler == null) continue;

                // 장식은 바닥 블록 위에 얹히므로 Y = baseY + 0.5f (블록 상단)
                var localPos = new Vector3(
                    x * cs - offset.x,
                    baseY + 0.5f,
                    rowZ * cs - offset.z);

                baseCtx.RawToken = raw;
                baseCtx.Cell     = new Vector2Int(x, rowZ);
                baseCtx.WorldPos = baseCtx.Parent.TransformPoint(localPos);

                handler.Execute(baseCtx);
            }
        }
    }

    /// <summary>방 그리드의 걷기셀(벽/구멍/천장 제외) 월드 AABB를 계산해 반 셀만큼 inset.
    /// 셀→월드 매핑은 Execute의 localPos 공식과 동일하게 맞춘다(좌표계 일관성).
    /// Y는 넓게(±50) 잡아 Contains/ClosestPoint가 X·Z만 사실상 제한하도록 한다.</summary>
    private static Bounds? ComputeFieldBounds(
        TileType[,] grid, int gridWidth, int gridHeight, float cs, float baseY, Transform parent, Vector3 offset)
    {
        if (parent == null) return null;

        int gw = Mathf.Min(gridWidth, grid.GetLength(0));
        int gh = Mathf.Min(gridHeight, grid.GetLength(1));

        bool any = false;
        Vector3 min = Vector3.positiveInfinity;
        Vector3 max = Vector3.negativeInfinity;

        for (int x = 0; x < gw; x++)
        for (int gz = 0; gz < gh; gz++)
        {
            var t = grid[x, gz];
            if (t == TileType.Wall || t == TileType.Empty || t == TileType.Ceiling) continue;

            var local = new Vector3(x * cs - offset.x, baseY, gz * cs - offset.z);
            var world = parent.TransformPoint(local);
            if (!any) { min = max = world; any = true; }
            else { min = Vector3.Min(min, world); max = Vector3.Max(max, world); }
        }
        if (!any) return null;

        // 벽·문에 딱 붙는 스폰 방지 — X·Z를 반 셀 안쪽으로. 너무 좁아지면 중앙으로 수렴.
        float inset = cs * 0.5f;
        min.x += inset; max.x -= inset;
        min.z += inset; max.z -= inset;
        if (min.x > max.x) { float m = (min.x + max.x) * 0.5f; min.x = max.x = m; }
        if (min.z > max.z) { float m = (min.z + max.z) * 0.5f; min.z = max.z = m; }

        var bounds = new Bounds();
        bounds.SetMinMax(new Vector3(min.x, baseY - 50f, min.z), new Vector3(max.x, baseY + 50f, max.z));
        return bounds;
    }

    private static string[] SplitRows(string gridCsv)
    {
        var raw = gridCsv.Contains(';')
            ? gridCsv.Split(';')
            : gridCsv.Split('\n');

        var result = new List<string>();
        foreach (var r in raw)
        {
            var trimmed = r.Trim();
            if (trimmed.Length > 0) result.Add(trimmed);
        }
        return result.ToArray();
    }
}
