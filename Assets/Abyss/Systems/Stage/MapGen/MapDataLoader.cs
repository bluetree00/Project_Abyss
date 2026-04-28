using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// grid_csv 문자열 ↔ TileType[,] 변환.
/// 행 구분: ;(세미콜론)  열 구분: ,(쉼마)
///
/// 기본 타일: F/W/O/P/B/S/N/E/X/T/C/./R/D (단일 문자)
/// 스포너 토큰: [Mm][cre]\d+  (예: Mc3, mr5, Me8)
///   - 첫 글자: M=확정, m=후보
///   - 두 번째:  c=Common, r=Rare, e=Elite (등급 상한)
///   - 나머지:   마릿수 (0=무제한)
/// 장식 토큰: d<code> (예: dt=Big Tree, dp=Small Tree, df=Fog, dl=Leaves, dm=Magic Lights)
///   - 파싱 시 TileType은 Floor로 기록되고 decorationInfos[cell] = "<code>" 저장
///   - 실제 장식 오브젝트는 Bootstrapper가 DecorationCatalog를 통해 후처리 스폰
/// 단독 "M" = "Mc0" 별칭 (하위호환).
/// </summary>
public static class MapDataLoader
{
    /// <summary>스포너 타일의 부가 메타. grid_csv 셀 토큰에서 파싱.</summary>
    public struct CellSpawnInfo
    {
        public MonsterGrade maxGrade;
        public int totalCount; // 0 = 무제한
    }

    /// <summary>grid_csv → TileType 2D 배열. 좌하단이 (0,0).
    /// spawnInfos에 스포너 셀(M/m 계열)의 메타 정보가 채워진다.
    /// decorationInfos에 장식 셀(d&lt;code&gt;)의 code 문자열이 채워진다.
    /// 장식 셀의 TileType은 Floor로 기록 — MapBuilder는 바닥만 깔고, 장식은 Bootstrapper가 후처리.</summary>
    public static TileType[,] Parse(
        string gridCsv,
        Dictionary<Vector2Int, CellSpawnInfo> spawnInfos = null,
        Dictionary<Vector2Int, string> decorationInfos = null)
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
            {
                string raw = cells[x].Trim();

                // 장식 토큰 d<code> — Floor로 기록 후 별도 dictionary에 code 저장
                if (decorationInfos != null && raw.Length >= 2 && raw[0] == 'd' && !IsBaseDecorationSymbol(raw))
                {
                    decorationInfos[new Vector2Int(x, rowZ)] = raw.Substring(1);
                    grid[x, rowZ] = TileType.Floor;
                    continue;
                }

                var tile = SymbolToTile(raw, out var info);
                grid[x, rowZ] = tile;

                if (spawnInfos != null &&
                    (tile == TileType.MonsterSpawn || tile == TileType.MonsterSpawnCandidate))
                    spawnInfos[new Vector2Int(x, rowZ)] = info;
            }
        }

        return grid;
    }

    /// <summary>단독 "d" 같이 base 기호와 충돌하는 케이스를 걸러냄. 현재 base 기호에 소문자 d는 없어 항상 false.</summary>
    private static bool IsBaseDecorationSymbol(string s) => false;

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

    /// <summary>셀 문자열을 TileType + (스포너 셀일 경우) CellSpawnInfo로 변환.</summary>
    private static TileType SymbolToTile(string s, out CellSpawnInfo info)
    {
        info = default;
        if (string.IsNullOrEmpty(s)) return TileType.Floor;

        // 스포너 토큰 [Mm][cre]?\d*  — 단독 "M" = "Mc0" 별칭
        if (s.Length >= 1 && (s[0] == 'M' || s[0] == 'm'))
        {
            if (TryParseSpawnerToken(s, out var tile, out info))
                return tile;
        }

        return s switch
        {
            "F" => TileType.Floor,
            "W" => TileType.Wall,
            "O" => TileType.Obstacle,
            "P" => TileType.PlayerSpawn,
            "B" => TileType.BossSpawn,
            "S"  => TileType.ShopStall,
            "Sw" => TileType.ShopStallWeapon,
            "Si" => TileType.ShopStallItem,
            "N" => TileType.NPCSpawn,
            "E" => TileType.Entrance,
            "X" => TileType.Exit,
            "T" => TileType.Trap,
            "C" => TileType.Chest,
            "." => TileType.Empty,
            "R" => TileType.BuffBox,
            "D" => TileType.BuffPedestal,
            _   => TileType.Floor,
        };
    }

    /// <summary>[Mm][cre]?\d* 토큰 파싱. 실패 시 false. 단독 M/m은 Common + count 0(무제한).</summary>
    private static bool TryParseSpawnerToken(string s, out TileType tile, out CellSpawnInfo info)
    {
        tile = default;
        info = default;
        if (s.Length == 0) return false;

        bool isCandidate = s[0] == 'm';
        if (!isCandidate && s[0] != 'M') return false;

        // 단독 M/m → Mc0 / mc0 별칭
        if (s.Length == 1)
        {
            info = new CellSpawnInfo { maxGrade = MonsterGrade.Common, totalCount = 0 };
            tile = isCandidate ? TileType.MonsterSpawnCandidate : TileType.MonsterSpawn;
            return true;
        }

        MonsterGrade grade;
        switch (s[1])
        {
            case 'c': case 'C': grade = MonsterGrade.Common; break;
            case 'r': case 'R': grade = MonsterGrade.Rare;   break;
            case 'e': case 'E': grade = MonsterGrade.Elite;  break;
            default: return false;
        }

        int count = 0;
        for (int i = 2; i < s.Length; i++)
        {
            char c = s[i];
            if (c < '0' || c > '9') return false;
            count = count * 10 + (c - '0');
        }

        info = new CellSpawnInfo { maxGrade = grade, totalCount = count };
        tile = isCandidate ? TileType.MonsterSpawnCandidate : TileType.MonsterSpawn;
        return true;
    }

    private static string TileToSymbol(TileType t) => t switch
    {
        TileType.Floor        => "F",
        TileType.Wall         => "W",
        TileType.Obstacle     => "O",
        TileType.MonsterSpawn => "M",
        TileType.PlayerSpawn  => "P",
        TileType.BossSpawn    => "B",
        TileType.ShopStall       => "S",
        TileType.ShopStallWeapon => "Sw",
        TileType.ShopStallItem   => "Si",
        TileType.NPCSpawn        => "N",
        TileType.Entrance     => "E",
        TileType.Exit         => "X",
        TileType.Trap         => "T",
        TileType.Chest        => "C",
        TileType.Empty        => ".",
        TileType.BuffBox      => "R",
        TileType.BuffPedestal => "D",
        _                     => "F",
    };
}
