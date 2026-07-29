using System.Collections.Generic;
using System.Text;

/// <summary>
/// grid_csv 문자열을 셀 단위로 회전/미러한다.
/// MapBuilder와 TokenParser가 같은 grid_csv 문자열을 각각 재파싱하므로,
/// 방향 변환은 트랜스폼이 아니라 이 문자열 레벨에서 한 번 적용해야 양쪽이 일치한다.
/// (트랜스폼 음수 스케일/회전은 콜라이더·NavMesh·블록 방향을 깨뜨림)
///
/// 행 구분: ';' 또는 '\n', 열 구분: ','. (MapDataLoader와 동일)
/// </summary>
public static class GridTransform
{
    /// <summary>좌우 미러(열 반전). 변형 + 턴 좌/우 결정에 사용.</summary>
    public static string MirrorX(string gridCsv)
    {
        if (string.IsNullOrWhiteSpace(gridCsv)) return gridCsv;

        var cells = ParseCells(gridCsv);
        foreach (var row in cells)
            System.Array.Reverse(row);
        return Serialize(cells);
    }

    /// <summary>시계방향 90° × quarterTurnsCW 회전. 음수/4 이상도 정규화.</summary>
    public static string Rotate(string gridCsv, int quarterTurnsCW)
    {
        if (string.IsNullOrWhiteSpace(gridCsv)) return gridCsv;

        int turns = ((quarterTurnsCW % 4) + 4) % 4;
        if (turns == 0) return gridCsv;

        var cells = ParseCells(gridCsv);
        for (int t = 0; t < turns; t++)
            cells = Rotate90CW(cells);
        return Serialize(cells);
    }

    // ── 내부 ──────────────────────────────────────

    private static string[][] ParseCells(string gridCsv)
    {
        var raw = gridCsv.Contains(';') ? gridCsv.Split(';') : gridCsv.Split('\n');
        var rows = new List<string[]>();
        foreach (var r in raw)
        {
            var t = r.Trim();
            if (t.Length == 0) continue;
            rows.Add(t.Split(','));
        }
        return rows.ToArray();
    }

    private static string[][] Rotate90CW(string[][] src)
    {
        int h = src.Length;
        int w = src[0].Length;

        // 회전 후 차원: 행 = w, 열 = h
        var dst = new string[w][];
        for (int r = 0; r < w; r++)
        {
            dst[r] = new string[h];
            for (int c = 0; c < h; c++)
                dst[r][c] = src[h - 1 - c][r];
        }
        return dst;
    }

    private static string Serialize(string[][] cells)
    {
        var sb = new StringBuilder();
        for (int r = 0; r < cells.Length; r++)
        {
            if (r > 0) sb.Append(';');
            for (int c = 0; c < cells[r].Length; c++)
            {
                if (c > 0) sb.Append(',');
                sb.Append(cells[r][c]);
            }
        }
        return sb.ToString();
    }
}
