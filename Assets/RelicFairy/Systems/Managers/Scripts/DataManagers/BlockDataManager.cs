using BackEnd;
using UnityEngine;
using System.Collections.Generic;
using System.IO;
using Cysharp.Threading.Tasks;
using System;
using LitJson;

/// <summary>
/// 뒤끝 CDN에서 MERLIN_RUNE_PIECE_DATA + MERLIN_RUNE_SYNERGY_DATA + MERLIN_RUNE_ZONE_MAP 로드.
/// </summary>
public class RuneDataManager
{
    private const string PieceFileName   = "merlin_rune_piece_data.json";
    private const string SynergyFileName = "merlin_rune_synergy_data.json";
    private const string ZoneMapFileName = "merlin_rune_zone_map.json";

    private string PieceFilePath    => Path.Combine(Application.persistentDataPath, PieceFileName);
    private string SynergyFilePath  => Path.Combine(Application.persistentDataPath, SynergyFileName);
    private string ZoneMapFilePath  => Path.Combine(Application.persistentDataPath, ZoneMapFileName);

    private Dictionary<int, RunePieceEntry>              _pieceById     = new();
    private Dictionary<string, List<RuneSynergyEntry>>   _synergyByZone = new();
    private List<RuneZoneMapEntry>                        _zoneMapRows   = new();

    public bool IsInitialized { get; private set; }

    // ── 초기화 ──────────────────────────────────

    public async UniTask InitializeAsync()
    {
        if (File.Exists(PieceFilePath))   LoadPiecesFromJson();
        if (File.Exists(SynergyFilePath)) LoadSynergiesFromJson();
        if (File.Exists(ZoneMapFilePath)) LoadZoneMapFromJson();

        try { await LoadFromServerAsync(); }
        catch (Exception e) { Debug.LogWarning($"[RuneDataManager] CDN 예외: {e.Message}"); }

        bool needFallback = _pieceById.Count == 0 || _synergyByZone.Count == 0 || _zoneMapRows.Count == 0;
        if (needFallback)
        {
            Debug.Log("[RuneDataManager] CDN 실패 — Addressables 폴백");

            if (_pieceById.Count == 0)
            {
                var pieceJson = await Managers.AddressableManager.TryLoadAssetAsync<TextAsset>("MERLIN_RUNE_PIECE_DATA");
                if (pieceJson != null)
                {
                    var col = JsonUtility.FromJson<RunePieceEntryCollection>(pieceJson.text);
                    if (col?.shapes != null)
                        foreach (var s in col.shapes)
                            _pieceById[s.shape_id] = s;
                }
            }

            if (_synergyByZone.Count == 0)
            {
                var synergyJson = await Managers.AddressableManager.TryLoadAssetAsync<TextAsset>("MERLIN_RUNE_SYNERGY_DATA");
                if (synergyJson != null)
                {
                    var col = JsonUtility.FromJson<RuneSynergyEntryCollection>(synergyJson.text);
                    if (col?.synergies != null)
                        foreach (var e in col.synergies)
                        {
                            if (string.IsNullOrEmpty(e.zone_id)) continue;
                            if (!_synergyByZone.TryGetValue(e.zone_id, out var list))
                            {
                                list = new List<RuneSynergyEntry>();
                                _synergyByZone[e.zone_id] = list;
                            }
                            list.Add(e);
                        }
                }
            }

            if (_zoneMapRows.Count == 0)
            {
                var zoneMapJson = await Managers.AddressableManager.TryLoadAssetAsync<TextAsset>("MERLIN_RUNE_ZONE_MAP");
                if (zoneMapJson != null)
                {
                    var col = JsonUtility.FromJson<RuneZoneMapEntryCollection>(zoneMapJson.text);
                    if (col?.rows != null)
                        _zoneMapRows = col.rows;
                }
            }
        }

        // Addressables 파일로 shape 보충/갱신 (CSV도 파싱, 모양 변경 시 덮어씀)
        try
        {
            var pieceAsset = await Managers.AddressableManager.TryLoadAssetAsync<TextAsset>("MERLIN_RUNE_PIECE_DATA");
            if (pieceAsset != null)
            {
                var col = JsonUtility.FromJson<RunePieceEntryCollection>(pieceAsset.text);
                if (col?.shapes == null)
                    col = ParseCsvToPieceCollection(pieceAsset.text);
                if (col?.shapes != null)
                {
                    bool changed = false;
                    foreach (var s in col.shapes)
                    {
                        if (!_pieceById.TryGetValue(s.shape_id, out var cur)
                            || cur.r1 != s.r1 || cur.r2 != s.r2 || cur.r3 != s.r3 || cur.r4 != s.r4)
                        {
                            _pieceById[s.shape_id] = s;
                            changed = true;
                        }
                    }
                    if (changed) SavePiecesToJson();
                }
            }
        }
        catch (Exception e) { Debug.LogWarning($"[RuneDataManager] Addressables shape merge 실패: {e.Message}"); }

        IsInitialized = true;
        Debug.Log($"[RuneDataManager] 초기화 완료. 룬 조각 {_pieceById.Count}종, 존 {_synergyByZone.Count}종, 존맵 {_zoneMapRows.Count}행");
    }

    // ── 조회 ──────────────────────────────────

    /// <summary>shape_id로 룬 조각 모양 조회.</summary>
    public RunePieceEntry GetPiece(int shapeId)
    {
        _pieceById.TryGetValue(shapeId, out var entry);
        return entry;
    }

    /// <summary>zone_id로 시너지 목록 조회.</summary>
    public List<RuneSynergyEntry> GetZoneSynergies(string zoneId)
    {
        _synergyByZone.TryGetValue(zoneId, out var list);
        return list;
    }

    /// <summary>헥사곤 존맵 전체 행 반환.</summary>
    public IReadOnlyList<RuneZoneMapEntry> GetZoneMapRows() => _zoneMapRows;

    /// <summary>전체 룬 조각 목록.</summary>
    public IReadOnlyDictionary<int, RunePieceEntry> GetAllPieces() => _pieceById;

    /// <summary>전체 시너지 존 목록.</summary>
    public IReadOnlyDictionary<string, List<RuneSynergyEntry>> GetAllZoneSynergies() => _synergyByZone;

    /// <summary>zone_id 목록 반환.</summary>
    public List<string> GetZoneIds()
    {
        return new List<string>(_synergyByZone.Keys);
    }

    // ── Compat aliases ──────────────────────────

    /// <summary>compat: GetPiece 별칭.</summary>
    public RunePieceEntry GetShape(int shapeId) => GetPiece(shapeId);

    /// <summary>compat: GetZoneSynergies 별칭 (gridId = zoneId).</summary>
    public List<RuneSynergyEntry> GetGrid(string zoneId) => GetZoneSynergies(zoneId);

    /// <summary>compat: GetZoneIds 별칭 (order 미적용 — zone 등록 순).</summary>
    public List<string> GetGridIdsSortedByOrder() => GetZoneIds();

    /// <summary>zone 대표 항목 반환 (첫 번째 시너지 엔트리).</summary>
    public RuneSynergyEntry GetGridMeta(string zoneId)
    {
        var list = GetZoneSynergies(zoneId);
        return list?.Count > 0 ? list[0] : null;
    }

    // ── Zone pattern helpers ──────────────────────────

    /// <summary>zone_id에 해당하는 존맵 셀 위치 목록 반환.</summary>
    public List<Vector2Int> GetZoneCellPositions(string zoneId)
    {
        char code = ElementDef.IdToCode(zoneId);
        var positions = new List<Vector2Int>();
        foreach (var row in _zoneMapRows)
        {
            if (string.IsNullOrEmpty(row.pattern)) continue;
            for (int c = 0; c < row.pattern.Length; c++)
                if (row.pattern[c] == code)
                    positions.Add(new Vector2Int(c, row.hex_row));
        }
        return positions;
    }

    /// <summary>셀 위치 목록 → rows01 string[] 변환 (GridPatternData 포맷: "1010" 형태).</summary>
    public static (string[] rows01, int rowCount, int colCount) BuildZonePattern(List<Vector2Int> positions)
    {
        if (positions == null || positions.Count == 0)
            return (new string[] { "1" }, 1, 1);

        int minX = int.MaxValue, minY = int.MaxValue;
        int maxX = int.MinValue, maxY = int.MinValue;
        foreach (var p in positions)
        {
            if (p.x < minX) minX = p.x;
            if (p.x > maxX) maxX = p.x;
            if (p.y < minY) minY = p.y;
            if (p.y > maxY) maxY = p.y;
        }
        int rowCount = maxY - minY + 1;
        int colCount = maxX - minX + 1;

        var grid = new char[rowCount][];
        for (int r = 0; r < rowCount; r++)
        {
            grid[r] = new char[colCount];
            for (int c = 0; c < colCount; c++)
                grid[r][c] = '0';
        }
        foreach (var p in positions)
            grid[p.y - minY][p.x - minX] = '1';

        var rows01 = new string[rowCount];
        for (int r = 0; r < rowCount; r++)
            rows01[r] = new string(grid[r]);
        return (rows01, rowCount, colCount);
    }

    /// <summary>RunePieceEntry에서 cellOffsets 배열 생성. r1~r4 패턴 파싱.</summary>
    public static Vector2Int[] ParseCellOffsets(RunePieceEntry entry)
    {
        if (entry == null) return System.Array.Empty<Vector2Int>();

        var rows = new List<string>();
        if (!string.IsNullOrEmpty(entry.r1)) rows.Add(entry.r1);
        if (!string.IsNullOrEmpty(entry.r2)) rows.Add(entry.r2);
        if (!string.IsNullOrEmpty(entry.r3)) rows.Add(entry.r3);
        if (!string.IsNullOrEmpty(entry.r4)) rows.Add(entry.r4);

        var offsets = new List<Vector2Int>();
        for (int r = 0; r < rows.Count; r++)
            for (int c = 0; c < rows[r].Length; c++)
                if (rows[r][c] == '1')
                    offsets.Add(new Vector2Int(c, -r));

        return offsets.ToArray();
    }

    // ── 로컬 저장/로드 ──────────────────────────

    private void LoadPiecesFromJson()
    {
        try
        {
            var json = File.ReadAllText(PieceFilePath);
            var col = JsonUtility.FromJson<RunePieceEntryCollection>(json);
            if (col?.shapes == null) return;
            _pieceById.Clear();
            foreach (var s in col.shapes) _pieceById[s.shape_id] = s;
        }
        catch (Exception e) { Debug.LogError($"[RuneDataManager] 룬 조각 로컬 로드 실패: {e.Message}"); }
    }

    private void LoadSynergiesFromJson()
    {
        try
        {
            var json = File.ReadAllText(SynergyFilePath);
            var col = JsonUtility.FromJson<RuneSynergyEntryCollection>(json);
            if (col?.synergies == null) return;
            _synergyByZone.Clear();
            foreach (var e in col.synergies)
            {
                if (string.IsNullOrEmpty(e.zone_id)) continue;
                if (!_synergyByZone.TryGetValue(e.zone_id, out var list))
                {
                    list = new List<RuneSynergyEntry>();
                    _synergyByZone[e.zone_id] = list;
                }
                list.Add(e);
            }
        }
        catch (Exception e) { Debug.LogError($"[RuneDataManager] 시너지 로컬 로드 실패: {e.Message}"); }
    }

    private void LoadZoneMapFromJson()
    {
        try
        {
            var json = File.ReadAllText(ZoneMapFilePath);
            var col = JsonUtility.FromJson<RuneZoneMapEntryCollection>(json);
            if (col?.rows == null) return;
            _zoneMapRows = col.rows;
        }
        catch (Exception e) { Debug.LogError($"[RuneDataManager] 존맵 로컬 로드 실패: {e.Message}"); }
    }

    private void SavePiecesToJson()
    {
        var col = new RunePieceEntryCollection { shapes = new List<RunePieceEntry>(_pieceById.Values) };
        File.WriteAllText(PieceFilePath, JsonUtility.ToJson(col, true));
    }

    private void SaveSynergiesToJson()
    {
        var all = new List<RuneSynergyEntry>();
        foreach (var list in _synergyByZone.Values) all.AddRange(list);
        var col = new RuneSynergyEntryCollection { synergies = all };
        File.WriteAllText(SynergyFilePath, JsonUtility.ToJson(col, true));
    }

    private void SaveZoneMapToJson()
    {
        var col = new RuneZoneMapEntryCollection { rows = _zoneMapRows };
        File.WriteAllText(ZoneMapFilePath, JsonUtility.ToJson(col, true));
    }

    // ── 서버 로드 ──────────────────────────────

    private async UniTask LoadFromServerAsync()
    {
        // 룬 조각 — 임시 딕셔너리에 모두 수집 후 전체 교체 (서버 삭제 반영)
        var newPieces = new Dictionary<int, RunePieceEntry>();
        int pieceLoaded = ChartLoader.Load("MERLIN_RUNE_PIECE_DATA", row =>
        {
            var entry = ParsePieceRow(row);
            if (entry != null)
                newPieces[entry.shape_id] = entry;
        });
        if (pieceLoaded > 0)
        {
            _pieceById = newPieces;
            SavePiecesToJson();
        }

        // 시너지 — 전체 교체
        var newSynergy = new Dictionary<string, List<RuneSynergyEntry>>();
        int synergyLoaded = ChartLoader.Load("MERLIN_RUNE_SYNERGY_DATA", row =>
        {
            var entry = ParseSynergyRow(row);
            if (entry == null || string.IsNullOrEmpty(entry.zone_id)) return;
            if (!newSynergy.TryGetValue(entry.zone_id, out var list))
            {
                list = new List<RuneSynergyEntry>();
                newSynergy[entry.zone_id] = list;
            }
            list.Add(entry);
        });
        if (synergyLoaded > 0)
        {
            _synergyByZone = newSynergy;
            SaveSynergiesToJson();
        }

        // 존맵 — 전체 교체 후 정렬
        var newZoneMap = new List<RuneZoneMapEntry>();
        int zoneMapLoaded = ChartLoader.Load("MERLIN_RUNE_ZONE_MAP", row =>
        {
            var entry = ParseZoneMapRow(row);
            if (entry != null) newZoneMap.Add(entry);
        });
        if (zoneMapLoaded > 0)
        {
            newZoneMap.Sort((a, b) => a.hex_row.CompareTo(b.hex_row));
            _zoneMapRows = newZoneMap;
            SaveZoneMapToJson();
        }

        await UniTask.CompletedTask;
    }

    private static RunePieceEntry ParsePieceRow(JsonData row)
    {
        try
        {
            return new RunePieceEntry
            {
                shape_id     = row.TryGetInt("shape_id"),
                shape_name   = row.TryGetString("shape_name"),
                r1           = row.TryGetString("r1"),
                r2           = row.TryGetString("r2"),
                r3           = row.TryGetString("r3"),
                r4           = row.TryGetString("r4"),
                cell_size    = row.TryGetFloat("cell_size"),
                stat_version = row.TryGetInt("stat_version"),
            };
        }
        catch { return null; }
    }

    private static RuneSynergyEntry ParseSynergyRow(JsonData row)
    {
        try
        {
            return new RuneSynergyEntry
            {
                zone_id      = row.TryGetString("zone_id"),
                zone_name    = row.TryGetString("zone_name"),
                threshold    = row.TryGetInt("threshold"),
                effect_type  = row.TryGetString("effect_type"),
                trigger      = row.TryGetString("trigger"),
                value        = row.TryGetFloat("value"),
                value2       = row.TryGetFloat("value2"),
                value3       = row.TryGetFloat("value3"),
                max_stack    = row.TryGetInt("max_stack"),
                duration     = row.TryGetFloat("duration"),
                description  = row.TryGetString("description"),
                stat_version = row.TryGetInt("stat_version"),
            };
        }
        catch { return null; }
    }

    private static RuneZoneMapEntry ParseZoneMapRow(JsonData row)
    {
        try
        {
            return new RuneZoneMapEntry
            {
                hex_row      = row.TryGetInt("hex_row"),
                pattern      = row.TryGetString("pattern"),
                stat_version = row.TryGetInt("stat_version"),
            };
        }
        catch { return null; }
    }

    /// <summary>compat: 구 grid rows01 파싱. 현재 스키마에서는 빈 배열 반환.</summary>
    public static int[][] ParseGridRows(RuneSynergyEntry meta) => System.Array.Empty<int[]>();

    // CSV 헤더: shape_id,shape_name,r1,r2,r3,r4,cell_size,stat_version
    private static RunePieceEntryCollection ParseCsvToPieceCollection(string csv)
    {
        var col = new RunePieceEntryCollection { shapes = new List<RunePieceEntry>() };
        var lines = csv.Split('\n');
        for (int i = 1; i < lines.Length; i++)
        {
            var line = lines[i].Trim();
            if (string.IsNullOrEmpty(line)) continue;
            var p = line.Split(',');
            if (p.Length < 6 || !int.TryParse(p[0].Trim(), out int id)) continue;
            col.shapes.Add(new RunePieceEntry
            {
                shape_id   = id,
                shape_name = p[1].Trim(),
                r1         = p[2].Trim(),
                r2         = p[3].Trim(),
                r3         = p[4].Trim(),
                r4         = p[5].Trim(),
                cell_size  = p.Length > 6 && float.TryParse(p[6].Trim(), out float cs) ? cs : 40f,
                stat_version = p.Length > 7 && int.TryParse(p[7].Trim(), out int sv) ? sv : 1,
            });
        }
        return col;
    }
}
