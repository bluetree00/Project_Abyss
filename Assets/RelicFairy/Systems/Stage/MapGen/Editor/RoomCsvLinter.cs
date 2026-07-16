using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 손저작 룸 풀 CSV(grid_csv)의 빌드 전 품질 게이트.
/// Docs/CHAPTER_1~4_ROOM_POOL.csv를 검사해 휴먼에러(미등록 토큰·공란·외곽 미폐쇄·
/// P 누락·비직사각·도어 미연결 등)를 콘솔/리포트에 보고한다.
///
/// 정본(유효 토큰) 단일화: 도어 추출은 <see cref="MapDataLoader.Parse"/>, 연결성은
/// <see cref="RoomDoorPlanner.Classify"/>를 재사용한다. 기하 타일 표(BaseTiles)는
/// "Validate Token Canon" 메뉴가 파서/레지스트리와 라운드트립으로 드리프트를 가드한다.
///
/// 이 도구는 에디터 전용(검증·문서 동기화)으로 런타임/세이브/런 계획에 영향이 없다.
/// </summary>
public static class RoomCsvLinter
{
    public enum Severity { Error, Warning }

    public struct Finding
    {
        public Severity severity;
        public string file;
        public int line;
        public string poolKey;
        public string message;
    }

    // ── 정본: 기하 타일 토큰 (출처 = MapDataLoader.SymbolToTile) ──────────────
    // 핸들러/파라메트릭(M·m·B·d·WP·CP·DR)은 아래 정규식/접두로 별도 판정.
    private static readonly HashSet<string> BaseTiles = new HashSet<string>
    {
        "F", "W", "O", "P", "B", "S", "Sw", "Si", "N", "E", "X", "T", "C",
        ".", "Pt", "R", "D", "CP", "WP", "SG", "CV",
    };

    private static readonly Regex SpawnerRegex = new Regex(@"^[Mm]([cCrReE][0-9]*)+$", RegexOptions.Compiled);
    private static readonly Regex DoorRegex    = new Regex(@"^DR[0-9]*$", RegexOptions.Compiled);
    private static readonly Regex PickupRegex  = new Regex(@"^(WP|CP)[0-9]+$", RegexOptions.Compiled);

    private const string DocsRelative = "RelicFairy/Docs";
    private static readonly string[] CsvNames =
    {
        "CHAPTER_1_ROOM_POOL.csv", "CHAPTER_2_ROOM_POOL.csv",
        "CHAPTER_3_ROOM_POOL.csv", "CHAPTER_4_ROOM_POOL.csv",
    };

    // ── 메뉴 ────────────────────────────────────────────────────────────────

    [MenuItem("Tools/RelicFairy/Validate Room CSVs")]
    public static void ValidateRoomCsvsMenu()
    {
        var findings = LintAllDocs(out int filesScanned, out int roomsScanned);
        int errors   = 0, warnings = 0;
        var sb = new StringBuilder();
        sb.AppendLine("# Room CSV Lint Report");
        sb.AppendLine($"- files: {filesScanned}, rooms: {roomsScanned}");

        foreach (var f in findings)
        {
            if (f.severity == Severity.Error) errors++; else warnings++;
            string loc = $"{f.file}:{f.line}  {f.poolKey}";
            string msg = $"[{(f.severity == Severity.Error ? "ERR" : "WARN")}] {loc} — {f.message}";
            sb.AppendLine(msg);
            if (f.severity == Severity.Error) Debug.LogError("[RoomCsvLint] " + msg);
            else                              Debug.LogWarning("[RoomCsvLint] " + msg);
        }

        string summary = $"[RoomCsvLint] 검사 완료 — 룸 {roomsScanned}개 / ERR {errors} / WARN {warnings}";
        sb.Insert(0, summary + "\n");

        string reportPath = Path.Combine(ProjectRoot(), "ROOM_CSV_LINT_REPORT.md");
        File.WriteAllText(reportPath, sb.ToString());

        if (errors == 0) Debug.Log(summary + $"\n리포트: {reportPath}");
        else             Debug.LogError(summary + $"\n리포트: {reportPath}");
    }

    [MenuItem("Tools/RelicFairy/Validate Token Canon (drift check)")]
    public static void ValidateTokenCanonMenu()
    {
        int mismatches = VerifyCanon();
        if (mismatches == 0)
            Debug.Log("[RoomCsvLint] 토큰 정본 동기화 OK — BaseTiles ↔ MapDataLoader.Parse ↔ TokenRegistry 일치.");
        else
            Debug.LogError($"[RoomCsvLint] 토큰 정본 드리프트 {mismatches}건 — 위 로그 확인 후 BaseTiles/문서 갱신 필요.");
    }

    // ── 공개 API ──────────────────────────────────────────────────────────────

    /// <summary>4개 Docs CSV를 검사해 Finding 목록 반환. 파일 부재 시 해당 파일만 건너뜀.</summary>
    public static List<Finding> LintAllDocs(out int filesScanned, out int roomsScanned)
    {
        var findings = new List<Finding>();
        filesScanned = 0;
        roomsScanned = 0;

        foreach (var name in CsvNames)
        {
            string path = Path.Combine(Application.dataPath, DocsRelative, name);
            if (!File.Exists(path))
            {
                Debug.LogWarning($"[RoomCsvLint] CSV 없음, 건너뜀: {name}");
                continue;
            }
            filesScanned++;
            roomsScanned += LintFile(path, name, findings);
        }
        return findings;
    }

    /// <summary>단일 CSV 파일 검사. 검사한 룸 수 반환.</summary>
    public static int LintFile(string path, string displayName, List<Finding> findings)
    {
        var lines = File.ReadAllLines(path);
        if (lines.Length < 2) return 0;

        var header = SplitCsvLine(lines[0]);
        var idx = new Dictionary<string, int>();
        for (int i = 0; i < header.Count; i++) idx[header[i].Trim()] = i;

        int Idx(string key) => idx.TryGetValue(key, out int v) ? v : -1;
        int cKey   = Idx("pool_key");
        int cCat   = Idx("category");
        int cW     = Idx("grid_width");
        int cH     = Idx("grid_height");
        int cGrid  = Idx("grid_csv");
        int cMax   = Idx("max_active_spawners");

        int rooms = 0;
        for (int ln = 1; ln < lines.Length; ln++)
        {
            if (string.IsNullOrWhiteSpace(lines[ln])) continue;
            var fields = SplitCsvLine(lines[ln]);
            string Field(int c) => (c >= 0 && c < fields.Count) ? fields[c] : "";

            string poolKey = Field(cKey).Trim();
            if (string.IsNullOrEmpty(poolKey)) continue;
            rooms++;

            LintRoom(
                displayName, ln + 1, poolKey,
                Field(cCat).Trim(),
                Field(cGrid),
                ParseIntOrNeg(Field(cW)),
                ParseIntOrNeg(Field(cH)),
                ParseIntOrNeg(Field(cMax)),
                findings);
        }
        return rooms;
    }

    // ── 룸 단위 규칙 ──────────────────────────────────────────────────────────

    private static void LintRoom(
        string file, int line, string poolKey, string category, string gridCsv,
        int declW, int declH, int maxActive, List<Finding> findings)
    {
        void Err(string m)  => findings.Add(new Finding { severity = Severity.Error,   file = file, line = line, poolKey = poolKey, message = m });
        void Warn(string m) => findings.Add(new Finding { severity = Severity.Warning, file = file, line = line, poolKey = poolKey, message = m });

        if (string.IsNullOrWhiteSpace(gridCsv)) { Err("grid_csv 비어 있음"); return; }

        var rows = SplitRows(gridCsv);
        if (rows.Count == 0) { Err("grid_csv 행 0개"); return; }

        int h = rows.Count;
        var widths = new int[h];
        for (int z = 0; z < h; z++) widths[z] = rows[z].Length;
        int w = widths[0]; // 파서와 동일: 첫 행 기준 폭

        // ④ 직사각 일관성
        bool rectOk = true;
        for (int z = 1; z < h; z++) if (widths[z] != w) { rectOk = false; break; }
        if (!rectOk)
        {
            var set = new SortedSet<int>(widths);
            Err($"비직사각: 행 길이 불일치 {{{string.Join(",", set)}}} (파서는 첫 행 폭 {w} 기준 → 정렬 붕괴)");
        }

        // ⑤ 선언 size ↔ 실제
        if ((declW > 0 || declH > 0) && (declW != w || declH != h))
            Warn($"선언 크기 {declW}x{declH} != 실제 {w}x{h} (자동계산이라 무시되지만 표기 수정 권장)");

        // 토큰 스캔
        int pCount = 0, bCount = 0, shopCount = 0, spawnCount = 0, mConfirmed = 0;
        int emptyCount = 0, innerDoor = 0, unknownTotal = 0;
        var unknownSamples = new List<string>();

        for (int z = 0; z < h; z++)
        {
            var cells = rows[z];
            int rowZ = h - 1 - z; // CSV 첫 행 = 윗줄(rowZ=h-1)
            for (int x = 0; x < cells.Length; x++)
            {
                string c = cells[x];
                if (c.Length == 0) { emptyCount++; continue; }

                if (!IsValidToken(c))
                {
                    unknownTotal++;
                    if (unknownSamples.Count < 8) unknownSamples.Add($"'{c}'@({x},r{z})");
                }

                if (c == "P") pCount++;
                else if (c == "B") bCount++;
                else if (c == "S" || c == "Sw" || c == "Si") shopCount++;

                if (c == "M" || c == "m" || SpawnerRegex.IsMatch(c))
                {
                    spawnCount++;
                    if (c[0] == 'M') mConfirmed++;
                }

                // 도어 내부배치 (경계가 아닌 곳의 DR)
                if (DoorRegex.IsMatch(c))
                {
                    bool onEdge = rowZ == h - 1 || rowZ == 0 || x == 0 || x == cells.Length - 1;
                    if (!onEdge) innerDoor++;
                }
            }
        }

        // ③ 미등록 토큰 / 공란
        if (unknownTotal > 0)
            Err($"미등록 토큰 {unknownTotal}개 (→Floor로 무음 폴백): {string.Join(", ", unknownSamples)}{(unknownTotal > unknownSamples.Count ? " …" : "")}");
        if (emptyCount > 0)
            Err($"공란 셀 {emptyCount}개 (공란 금지 — 무음 Floor)");

        // ① P 정확히 1개
        if (pCount != 1) Err($"P(플레이어 시작) {pCount}개 — 정확히 1개 필요");

        // ② 외곽 폐쇄 (경계 셀은 W 또는 DR만)
        int borderBreach = CountBorderBreaches(rows, w, h);
        if (borderBreach > 0)
            Err($"외곽 미폐쇄: 비-벽/문 경계 셀 {borderBreach}개 (낙사 위험)");

        // ⑥ 문 내부배치
        if (innerDoor > 0)
            Err($"문(DR) 내부 배치 {innerDoor}개 — 경계에서만 유효(InferDoorEdge→Floor 소실)");

        // ⑦ 도어 연결성 — 파서/플래너 재사용
        var doors = new Dictionary<Vector2Int, DoorInfo>();
        MapDataLoader.Parse(gridCsv, null, null, doors);
        var cls = RoomDoorPlanner.Classify(doors);
        bool isBoss = category == "Boss";
        if (!cls.entrance.HasValue)
            Err(isBoss ? "Boss방 입구(South 문) 없음" : "입구(South 문) 없음 — 방 진입 불가");
        if (!isBoss && !cls.forward.HasValue)
            Err("직진 출구(North 문) 없음 — 다음 방 진행 불가");

        // ⑧ 카테고리 계약
        if (category == "Boss" && bCount < 1)            Err("Boss방에 B(보스 스폰) 없음");
        if (category == "Shop" && shopCount < 1)         Err("Shop방에 매대(Sw/Si) 없음");
        if ((category == "Normal" || category == "Elite") && spawnCount < 1)
            Err($"{category}방에 스포너(M/m) 없음 — 전투 클리어 불가");

        // ⑨ max_active_spawners 정합
        if (maxActive > 0 && mConfirmed > maxActive)
            Warn($"확정 스포너 M {mConfirmed}개 > max {maxActive} — 후보(m)는 전부 Floor 처리");
        if (maxActive > 0 && spawnCount > 0 && maxActive > spawnCount)
            Warn($"max {maxActive} > 스포너 총수 {spawnCount} — 도달 불가 상한");
    }

    /// <summary>경계(외곽 링) 셀 중 Wall도 도어(DR)도 아닌 칸 수.</summary>
    private static int CountBorderBreaches(List<string[]> rows, int w, int h)
    {
        bool OkBorder(string c) => c == "W" || DoorRegex.IsMatch(c);
        string Cell(int z, int x)
        {
            var r = rows[z];
            return x < r.Length ? r[x] : "";
        }

        int breach = 0;
        for (int x = 0; x < w; x++)
        {
            if (!OkBorder(Cell(0, x)))     breach++;     // CSV 첫 행(윗줄)
            if (h > 1 && !OkBorder(Cell(h - 1, x))) breach++; // 마지막 행(아랫줄)
        }
        for (int z = 0; z < h; z++)
        {
            if (!OkBorder(Cell(z, 0)))         breach++;
            if (w > 1 && !OkBorder(Cell(z, w - 1))) breach++;
        }
        return breach;
    }

    // ── 토큰 유효성 (정본 단일화) ─────────────────────────────────────────────

    /// <summary>런타임이 의미를 부여하는 토큰인지. false면 SymbolToTile default→Floor로 무음 소실.</summary>
    public static bool IsValidToken(string t)
    {
        if (string.IsNullOrEmpty(t)) return false;
        if (BaseTiles.Contains(t)) return true;
        if (t == "M" || t == "m") return true;
        if (SpawnerRegex.IsMatch(t)) return true;
        if (DoorRegex.IsMatch(t)) return true;
        if (PickupRegex.IsMatch(t)) return true;
        if (t.Length >= 2 && t[0] == 'd') return true; // 장식 d<code>
        return false;
    }

    // ── 토큰 정본 드리프트 자가검증 ───────────────────────────────────────────

    /// <summary>BaseTiles 표를 MapDataLoader.Parse(라운드트립) + TokenRegistry와 대조. 불일치 수 반환.</summary>
    public static int VerifyCanon()
    {
        int mismatch = 0;

        // (1) 기하 타일: Parse가 기대 TileType을 내는지
        var expect = new Dictionary<string, TileType>
        {
            { "F", TileType.Floor }, { "W", TileType.Wall }, { "O", TileType.Obstacle },
            { "P", TileType.PlayerSpawn }, { "B", TileType.BossSpawn }, { "S", TileType.ShopStall },
            { "Sw", TileType.ShopStallWeapon }, { "Si", TileType.ShopStallItem }, { "N", TileType.NPCSpawn },
            { "E", TileType.Entrance }, { "X", TileType.Exit }, { "T", TileType.Trap }, { "C", TileType.Chest },
            { ".", TileType.Empty }, { "Pt", TileType.Empty }, { "R", TileType.BuffBox },
            { "D", TileType.BuffPedestal }, { "CP", TileType.CharacterPickup },
            { "WP", TileType.WeaponPickup }, { "SG", TileType.StartGate },
        };
        foreach (var kv in expect)
        {
            var grid = MapDataLoader.Parse(kv.Key);
            var got = grid != null ? grid[0, 0] : TileType.Floor;
            if (got != kv.Value)
            {
                mismatch++;
                Debug.LogError($"[RoomCsvLint] 기하 토큰 드리프트: '{kv.Key}' 기대 {kv.Value} != 파서 {got} — BaseTiles 또는 SymbolToTile 변경됨.");
            }
        }

        // (2) 핸들러 토큰: 레지스트리가 린터가 아는 코드만 갖는지
        var known = new HashSet<string> { "M", "m", "B", "d", "WP", "CP", "Pt", "CV" };
        foreach (var kv in TokenRegistry.ExactHandlers)
            if (!known.Contains(kv.Key))
            {
                mismatch++;
                Debug.LogError($"[RoomCsvLint] 신규 핸들러 토큰 '{kv.Key}' — 린터(IsValidToken)가 모름. 정본 갱신 필요.");
            }
        foreach (var e in TokenRegistry.PrefixHandlers)
            if (!known.Contains(e.Code))
            {
                mismatch++;
                Debug.LogError($"[RoomCsvLint] 신규 접두 핸들러 '{e.Code}' — 린터(IsValidToken)가 모름. 정본 갱신 필요.");
            }

        return mismatch;
    }

    // ── CSV / 그리드 파싱 헬퍼 ────────────────────────────────────────────────

    /// <summary>따옴표 인식 CSV 한 줄 → 필드 목록 (grid_csv의 내부 쉼표·세미콜론 보존).</summary>
    private static List<string> SplitCsvLine(string line)
    {
        var fields = new List<string>();
        var sb = new StringBuilder();
        bool inQuotes = false;
        for (int i = 0; i < line.Length; i++)
        {
            char ch = line[i];
            if (inQuotes)
            {
                if (ch == '"')
                {
                    if (i + 1 < line.Length && line[i + 1] == '"') { sb.Append('"'); i++; }
                    else inQuotes = false;
                }
                else sb.Append(ch);
            }
            else
            {
                if (ch == '"') inQuotes = true;
                else if (ch == ',') { fields.Add(sb.ToString()); sb.Clear(); }
                else sb.Append(ch);
            }
        }
        fields.Add(sb.ToString());
        return fields;
    }

    /// <summary>grid_csv → 행별 셀 배열. MapDataLoader.SplitRows 규약(;|\n, trim, 빈 행 제거)을 따른다.</summary>
    private static List<string[]> SplitRows(string gridCsv)
    {
        var raw = gridCsv.Contains(";") ? gridCsv.Split(';') : gridCsv.Split('\n');
        var rows = new List<string[]>();
        foreach (var r in raw)
        {
            var trimmed = r.Trim();
            if (trimmed.Length == 0) continue;
            var cells = trimmed.Split(',');
            for (int i = 0; i < cells.Length; i++) cells[i] = cells[i].Trim();
            rows.Add(cells);
        }
        return rows;
    }

    private static int ParseIntOrNeg(string s) => int.TryParse(s?.Trim(), out int v) ? v : -1;

    private static string ProjectRoot() => Directory.GetParent(Application.dataPath).FullName;
}
