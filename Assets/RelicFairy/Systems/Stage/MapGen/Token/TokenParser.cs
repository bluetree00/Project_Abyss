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
    /// grid_csv 전체를 순회해 등록된 핸들러가 있는 토큰만 Execute한다.
    /// </summary>
    /// <param name="gridCsv">MapRoomEntry.grid_csv 또는 ZoneLayoutEntry.grid_csv</param>
    /// <param name="gridWidth">grid.GetLength(0) — X축 셀 수</param>
    /// <param name="gridHeight">grid.GetLength(1) — Z축 셀 수</param>
    /// <param name="baseCtx">공유 데이터가 채워진 컨텍스트. RawToken/Cell/WorldPos는 이 메서드가 덮어씀.</param>
    public static void Execute(
        string gridCsv,
        int gridWidth,
        int gridHeight,
        TokenContext baseCtx)
    {
        if (string.IsNullOrWhiteSpace(gridCsv) || baseCtx?.Parent == null) return;

        TokenRegistry.EnsureInitialized();

        var rows   = SplitRows(gridCsv);
        int h      = rows.Length;
        float cs   = baseCtx.CellSize;
        float baseY = baseCtx.BaseY;

        // 맵 중앙이 parent.position에 오도록 — MapBuilder와 동일한 오프셋
        var offset = new Vector3((gridWidth - 1) * 0.5f * cs, 0f, (gridHeight - 1) * 0.5f * cs);

        for (int z = 0; z < h; z++)
        {
            var cells = rows[z].Split(',');
            int rowZ  = h - 1 - z; // csv 첫 줄 = 맨 윗줄 → z 반전

            for (int x = 0; x < cells.Length && x < gridWidth; x++)
            {
                string raw = cells[x].Trim();
                if (string.IsNullOrEmpty(raw) || raw == ".") continue;

                var handler = TokenRegistry.Resolve(raw);
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
