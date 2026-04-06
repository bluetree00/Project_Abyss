using UnityEngine;

/// <summary>
/// grid_csv 문자열 ↔ TileType[,] 변환.
/// 행 구분: ;(세미콜론)  열 구분: ,(쉼마)
///
/// 예: "W,W,W;W,F,W;W,W,W"
///   → 3×3 그리드, 가운데만 Floor
/// </summary>
public static class MapDataLoader
{
    /// <summary>grid_csv → TileType 2D 배열. 좌하단이 (0,0).</summary>
    public static TileType[,] Parse(string gridCsv)
    {
        if (string.IsNullOrWhiteSpace(gridCsv))
            return null;

        // 세미콜론(;) 또는 개행(\n) 모두 행 구분으로 허용
        var rows = SplitRows(gridCsv);
        int h = rows.Length;
        int w = rows[0].Split(',').Length;
        var grid = new TileType[w, h];

        for (int z = 0; z < h; z++)
        {
            var cells = rows[z].Split(',');
            int rowZ = h - 1 - z; // csv 첫 줄 = 맨 윗줄 → z 반전

            for (int x = 0; x < Mathf.Min(cells.Length, w); x++)
                grid[x, rowZ] = SymbolToTile(cells[x].Trim());
        }

        return grid;
    }

    /// <summary>TileType 2D 배열 → grid_csv 문자열.</summary>
    public static string Serialize(TileType[,] grid)
    {
        int w = grid.GetLength(0);
        int h = grid.GetLength(1);

        var sb = new System.Text.StringBuilder();

        for (int z = h - 1; z >= 0; z--)
        {
            for (int x = 0; x < w; x++)
            {
                if (x > 0) sb.Append(',');
                sb.Append(TileToSymbol(grid[x, z]));
            }
            if (z > 0) sb.Append(';');
        }

        return sb.ToString();
    }

    /// <summary>그리드 크기 반환 (width, height).</summary>
    public static Vector2Int GetSize(string gridCsv)
    {
        if (string.IsNullOrWhiteSpace(gridCsv))
            return Vector2Int.zero;

        var rows = SplitRows(gridCsv);
        int h = rows.Length;
        int w = rows[0].Split(',').Length;
        return new Vector2Int(w, h);
    }

    /// <summary>특정 TileType의 모든 좌표를 반환.</summary>
    public static System.Collections.Generic.List<Vector2Int> FindAll(TileType[,] grid, TileType type)
    {
        var result = new System.Collections.Generic.List<Vector2Int>();
        int w = grid.GetLength(0);
        int h = grid.GetLength(1);

        for (int x = 0; x < w; x++)
            for (int z = 0; z < h; z++)
                if (grid[x, z] == type)
                    result.Add(new Vector2Int(x, z));

        return result;
    }

    /// <summary>특정 TileType의 첫 좌표 반환. 없으면 (-1,-1).</summary>
    public static Vector2Int FindFirst(TileType[,] grid, TileType type)
    {
        int w = grid.GetLength(0);
        int h = grid.GetLength(1);

        for (int x = 0; x < w; x++)
            for (int z = 0; z < h; z++)
                if (grid[x, z] == type)
                    return new Vector2Int(x, z);

        return new Vector2Int(-1, -1);
    }

    // ── 행 분리 (;와 \n 모두 지원) ─────────────────

    private static string[] SplitRows(string gridCsv)
    {
        var raw = gridCsv.Contains(';')
            ? gridCsv.Split(';')
            : gridCsv.Split('\n');

        // 빈 행 제거 (끝에 개행이 있을 수 있음)
        var result = new System.Collections.Generic.List<string>();
        foreach (var r in raw)
        {
            var trimmed = r.Trim();
            if (trimmed.Length > 0) result.Add(trimmed);
        }
        return result.ToArray();
    }

    // ── 기호 ↔ TileType 변환 ──────────────────────

    private static TileType SymbolToTile(string s) => s switch
    {
        "F" => TileType.Floor,
        "W" => TileType.Wall,
        "O" => TileType.Obstacle,
        "M" => TileType.MonsterSpawn,
        "P" => TileType.PlayerSpawn,
        "B" => TileType.BossSpawn,
        "S" => TileType.ShopStall,
        "N" => TileType.NPCSpawn,
        "E" => TileType.Entrance,
        "X" => TileType.Exit,
        "T" => TileType.Trap,
        "C" => TileType.Chest,
        "." => TileType.Empty,
        _   => TileType.Floor,
    };

    private static string TileToSymbol(TileType t) => t switch
    {
        TileType.Floor        => "F",
        TileType.Wall         => "W",
        TileType.Obstacle     => "O",
        TileType.MonsterSpawn => "M",
        TileType.PlayerSpawn  => "P",
        TileType.BossSpawn    => "B",
        TileType.ShopStall    => "S",
        TileType.NPCSpawn     => "N",
        TileType.Entrance     => "E",
        TileType.Exit         => "X",
        TileType.Trap         => "T",
        TileType.Chest        => "C",
        TileType.Empty        => ".",
        _                     => "F",
    };
}
