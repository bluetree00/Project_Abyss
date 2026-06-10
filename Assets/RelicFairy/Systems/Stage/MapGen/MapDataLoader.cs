using System.Collections.Generic;
using UnityEngine;
// ReSharper disable SwitchStatementMissingSomeEnumCasesNoDefault

/// <summary>
/// grid_csv 문자열 ↔ TileType[,] 변환.
/// 행 구분: ;(세미콜론)  열 구분: ,(쉼마)
///
/// 기본 타일: F/W/O/P/B/S/N/E/X/T/C/./R/D (단일 문자)
/// 스포너 토큰: [Mm]([cre]\d*)+
///   - 첫 글자: M=확정, m=후보
///   - 세그먼트: [cre]\d*  c=Common / r=Rare / e=Elite + 마릿수(생략 시 0=무제한)
///   - 세그먼트 1개: Mc3  → 단일 등급·수량 (레거시 루프 모드 또는 1-웨이브)
///   - 세그먼트 2개+: Mc3r5e2 → 웨이브 배열 (웨이브 수 = 세그먼트 수)
/// 장식 토큰: d<code> (예: dt=Big Tree, dp=Small Tree, df=Fog, dl=Leaves, dm=Magic Lights)
///   - 파싱 시 TileType은 Floor로 기록되고 decorationInfos[cell] = "<code>" 저장
///   - 실제 장식 오브젝트는 Bootstrapper가 DecorationCatalog를 통해 후처리 스폰
/// 단독 "M" = "Mc0" 별칭 (하위호환).
/// </summary>
public static class MapDataLoader
{
    /// <summary>웨이브 하나의 등급+마릿수. CellSpawnInfo.waves 배열 원소.</summary>
    public struct WaveCellConfig
    {
        public MonsterGrade grade;
        public int count; // 0 = 무제한 (레거시)
    }

    /// <summary>스포너 타일의 부가 메타. grid_csv 셀 토큰에서 파싱.
    /// waves가 null이면 레거시 단일 등급/수량 모드 (maxGrade, totalCount 사용).
    /// waves가 non-null이면 웨이브 배열 모드 — maxGrade/totalCount는 무시.</summary>
    public struct CellSpawnInfo
    {
        public MonsterGrade maxGrade;  // 레거시 호환 (waves==null 일 때)
        public int totalCount;         // 레거시 호환 (waves==null 일 때)
        public WaveCellConfig[] waves; // null=레거시, 길이≥2=웨이브 모드
    }

    /// <summary>grid_csv → TileType 2D 배열. 좌하단이 (0,0).
    /// spawnInfos에 스포너 셀(M/m 계열)의 메타 정보가 채워진다.
    /// decorationInfos에 장식 셀(d&lt;code&gt;)의 code 문자열이 채워진다.
    /// 장식 셀의 TileType은 Floor로 기록 — MapBuilder는 바닥만 깔고, 장식은 Bootstrapper가 후처리.</summary>
    public static TileType[,] Parse(
        string gridCsv,
        Dictionary<Vector2Int, CellSpawnInfo> spawnInfos = null,
        Dictionary<Vector2Int, string> decorationInfos = null,
        Dictionary<Vector2Int, DoorInfo> doorInfos = null)
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

                // 문 토큰 DR<width> — 벽 위 문 앵커. 기본 폐쇄(Wall) + doorInfos에 기록.
                // 연결 파이프라인이 선택된 문만 Floor로 개방한다.
                if (doorInfos != null && raw.Length >= 2 && raw[0] == 'D' && raw[1] == 'R')
                {
                    int doorWidth = 3;
                    if (raw.Length > 2) int.TryParse(raw.Substring(2), out doorWidth);
                    if (doorWidth <= 0) doorWidth = 3;

                    var edge = InferDoorEdge(x, rowZ, w, h);
                    if (edge.HasValue)
                    {
                        doorInfos[new Vector2Int(x, rowZ)] = new DoorInfo { edge = edge.Value, width = doorWidth };
                        grid[x, rowZ] = TileType.Wall; // 기본 폐쇄 — 연결 시에만 개방
                        continue;
                    }
                    // 내부 셀(엣지 아님)은 무효 → 일반 처리로 폴백
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

    /// <summary>문 앵커 셀의 위치로 엣지(방향)를 추론. 벽 링(외곽) 위가 아니면 null.</summary>
    private static DoorEdge? InferDoorEdge(int x, int rowZ, int w, int h)
    {
        if (rowZ == h - 1) return DoorEdge.North;
        if (rowZ == 0)     return DoorEdge.South;
        if (x == 0)        return DoorEdge.West;
        if (x == w - 1)    return DoorEdge.East;
        return null; // 내부 셀: 무효
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
            "Pt" => TileType.Empty,   // PitTrigger — 바닥 없음 + PostBuild 트리거 배치
            "R"  => TileType.BuffBox,
            "D"  => TileType.BuffPedestal,
            "CP" => TileType.CharacterPickup,
            "WP" => TileType.WeaponPickup,
            "CV" => TileType.Floor,   // 서약 제단 — 바닥 위에 CovenantAltarHandler가 PostBuild 스폰
            "SG" => TileType.StartGate,
            _    => UnknownToFloor(s),
        };
    }

    // 미등록 토큰이 default로 떨어질 때 1회 경고하는 dedup 집합 (도메인 리로드마다 초기화).
    private static readonly HashSet<string> _warnedUnknownTokens = new HashSet<string>();

    /// <summary>등록되지 않은 토큰을 Floor로 폴백하되, 무음 소실을 막기 위해 토큰별 1회 경고한다.
    /// 반환값(Floor)·렌더 동작은 기존과 동일 — 진단 가시화만 추가.
    /// 장식 d&lt;code&gt;·문 DR&lt;w&gt;는 상위 레이어(TokenParser/doorInfos)가 처리하는 정상 토큰이며,
    /// 호출부가 해당 dictionary를 넘기지 않으면 합법적으로 이 분기에 도달하므로 경고에서 제외한다.</summary>
    private static TileType UnknownToFloor(string s)
    {
        bool handledElsewhere =
            (s.Length >= 2 && s[0] == 'd') ||                 // 장식 d<code>
            (s.Length >= 2 && s[0] == 'D' && s[1] == 'R');    // 문 DR<width>
        if (!handledElsewhere && _warnedUnknownTokens.Add(s))
            Debug.LogWarning($"[MapDataLoader] 미등록 토큰 '{s}' → Floor 폴백(무음 소실 방지 경고). grid_csv 오타 가능성 — Tools/RelicFairy/Validate Room CSVs 확인 권장.");
        return TileType.Floor;
    }

    /// <summary>[Mm]([cre]\d*)+ 토큰 파싱. 실패 시 false.
    /// 단독 M/m → Common count 0 (무제한 레거시).
    /// 세그먼트 1개 → 레거시 모드 (waves=null).
    /// 세그먼트 2개+ → 웨이브 배열 모드 (waves 채움, maxGrade/totalCount 레거시 필드는 미사용).</summary>
    private static bool TryParseSpawnerToken(string s, out TileType tile, out CellSpawnInfo info)
    {
        tile = default;
        info = default;
        if (s.Length == 0) return false;

        bool isCandidate = s[0] == 'm';
        if (!isCandidate && s[0] != 'M') return false;

        tile = isCandidate ? TileType.MonsterSpawnCandidate : TileType.MonsterSpawn;

        // 단독 M/m → Mc0 레거시
        if (s.Length == 1)
        {
            info = new CellSpawnInfo { maxGrade = MonsterGrade.Common, totalCount = 0, waves = null };
            return true;
        }

        // [cre]\d* 세그먼트를 반복 파싱
        var waveList = new List<WaveCellConfig>();
        int pos = 1;
        while (pos < s.Length)
        {
            MonsterGrade grade;
            switch (s[pos])
            {
                case 'c': case 'C': grade = MonsterGrade.Common; break;
                case 'r': case 'R': grade = MonsterGrade.Rare;   break;
                case 'e': case 'E': grade = MonsterGrade.Elite;  break;
                default: return false;
            }
            pos++;

            int count = 0;
            while (pos < s.Length && s[pos] >= '0' && s[pos] <= '9')
            {
                count = count * 10 + (s[pos] - '0');
                pos++;
            }

            waveList.Add(new WaveCellConfig { grade = grade, count = count });
        }

        if (waveList.Count == 0) return false;

        if (waveList.Count == 1)
        {
            // 세그먼트 1개 → 레거시 단일 등급/수량 (기존 동작 유지)
            info = new CellSpawnInfo
            {
                maxGrade   = waveList[0].grade,
                totalCount = waveList[0].count,
                waves      = null,
            };
        }
        else
        {
            // 세그먼트 2개+ → 웨이브 배열 모드
            info = new CellSpawnInfo
            {
                maxGrade   = waveList[0].grade, // 참고용 (실제 사용 안 함)
                totalCount = 0,
                waves      = waveList.ToArray(),
            };
        }

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
        TileType.BuffBox         => "R",
        TileType.BuffPedestal    => "D",
        TileType.CharacterPickup => "CP",
        TileType.WeaponPickup    => "WP",
        TileType.StartGate       => "SG",
        _                        => "F",
    };
}
