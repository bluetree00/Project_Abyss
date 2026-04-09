using BackEnd;
using UnityEngine;
using System.Collections.Generic;
using System.IO;
using Cysharp.Threading.Tasks;
using System;
using LitJson;

/// <summary>
/// 뒤끝 CDN에서 BLOCK_SHAPE_DATA + BLOCK_GRID_DATA 로드.
/// PlayerDataManager와 동일 패턴.
/// </summary>
public class BlockDataManager
{
    private const string ShapeFileName = "block_shape_data.json";
    private const string GridFileName  = "block_grid_data.json";
    private string ShapeFilePath => Path.Combine(Application.persistentDataPath, ShapeFileName);
    private string GridFilePath  => Path.Combine(Application.persistentDataPath, GridFileName);

    private const string ShapeChartId = "235417";
    private const string GridChartId  = "235420";

    private Dictionary<int, BlockShapeEntry> _shapeById = new();
    private Dictionary<string, List<BlockGridEntry>> _gridById = new();

    public bool IsInitialized { get; private set; }

    // ── 초기화 ──────────────────────────────────

    public async UniTask InitializeAsync()
    {
        if (File.Exists(ShapeFilePath)) LoadShapesFromJson();
        if (File.Exists(GridFilePath))  LoadGridsFromJson();

        try { await LoadFromServerAsync(); }
        catch (Exception e) { Debug.LogWarning($"[BlockDataManager] CDN 예외: {e.Message}"); }

        if (_shapeById.Count == 0)
        {
            Debug.Log("[BlockDataManager] CDN 실패 — Resources 폴백");
            var shapeJson = Resources.Load<TextAsset>("BLOCK_SHAPE_DATA");
            if (shapeJson != null)
            {
                var col = JsonUtility.FromJson<BlockShapeEntryCollection>(shapeJson.text);
                if (col?.shapes != null)
                    foreach (var s in col.shapes)
                        _shapeById[s.shape_id] = s;
            }
            var gridJson = Resources.Load<TextAsset>("BLOCK_GRID_DATA");
            if (gridJson != null)
            {
                var col = JsonUtility.FromJson<BlockGridEntryCollection>(gridJson.text);
                if (col?.grids != null)
                    foreach (var g in col.grids)
                    {
                        if (string.IsNullOrEmpty(g.grid_id)) continue;
                        if (!_gridById.TryGetValue(g.grid_id, out var list))
                        {
                            list = new List<BlockGridEntry>();
                            _gridById[g.grid_id] = list;
                        }
                        list.Add(g);
                    }
            }
        }

        IsInitialized = true;
        Debug.Log($"[BlockDataManager] 초기화 완료. 블록 {_shapeById.Count}종, 그리드 {_gridById.Count}종");
    }

    // ── 조회 ──────────────────────────────────

    /// <summary>shape_id로 블록 모양 조회.</summary>
    public BlockShapeEntry GetShape(int shapeId)
    {
        _shapeById.TryGetValue(shapeId, out var entry);
        return entry;
    }

    /// <summary>grid_id로 시너지 그리드 전체 슬롯 조회.</summary>
    public List<BlockGridEntry> GetGrid(string gridId)
    {
        _gridById.TryGetValue(gridId, out var list);
        return list;
    }

    /// <summary>grid_id로 slot 1 엔트리 반환 (그리드 모양 + 메타 정보).</summary>
    public BlockGridEntry GetGridMeta(string gridId)
    {
        if (!_gridById.TryGetValue(gridId, out var list)) return null;
        foreach (var e in list)
            if (e.slot == 1) return e;
        return list.Count > 0 ? list[0] : null;
    }

    /// <summary>전체 블록 모양 목록.</summary>
    public IReadOnlyDictionary<int, BlockShapeEntry> GetAllShapes() => _shapeById;

    /// <summary>전체 시너지 그리드 목록.</summary>
    public IReadOnlyDictionary<string, List<BlockGridEntry>> GetAllGrids() => _gridById;

    /// <summary>order 순으로 정렬된 grid_id 목록 반환.</summary>
    public List<string> GetGridIdsSortedByOrder()
    {
        var metas = new List<(string id, int order)>();
        foreach (var kvp in _gridById)
        {
            var meta = GetGridMeta(kvp.Key);
            if (meta != null)
                metas.Add((kvp.Key, meta.order));
        }
        metas.Sort((a, b) => a.order.CompareTo(b.order));

        var result = new List<string>(metas.Count);
        foreach (var m in metas) result.Add(m.id);
        return result;
    }

    /// <summary>BlockShapeEntry에서 cellOffsets 배열 생성. r1~r4 패턴 파싱.</summary>
    public static Vector2Int[] ParseCellOffsets(BlockShapeEntry entry)
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

    /// <summary>BlockGridEntry에서 GridPatternSO용 rows01 배열 생성. g1~g8 패턴 파싱.</summary>
    public static string[] ParseGridRows(BlockGridEntry entry)
    {
        if (entry == null) return System.Array.Empty<string>();

        var rows = new List<string>();
        if (!string.IsNullOrEmpty(entry.g1)) rows.Add(entry.g1);
        if (!string.IsNullOrEmpty(entry.g2)) rows.Add(entry.g2);
        if (!string.IsNullOrEmpty(entry.g3)) rows.Add(entry.g3);
        if (!string.IsNullOrEmpty(entry.g4)) rows.Add(entry.g4);
        if (!string.IsNullOrEmpty(entry.g5)) rows.Add(entry.g5);
        if (!string.IsNullOrEmpty(entry.g6)) rows.Add(entry.g6);
        if (!string.IsNullOrEmpty(entry.g7)) rows.Add(entry.g7);
        if (!string.IsNullOrEmpty(entry.g8)) rows.Add(entry.g8);

        return rows.ToArray();
    }

    // ── 로컬 저장/로드 ──────────────────────────

    private void LoadShapesFromJson()
    {
        try
        {
            var json = File.ReadAllText(ShapeFilePath);
            var col = JsonUtility.FromJson<BlockShapeEntryCollection>(json);
            if (col?.shapes == null) return;
            _shapeById.Clear();
            foreach (var s in col.shapes) _shapeById[s.shape_id] = s;
        }
        catch (Exception e) { Debug.LogError($"[BlockDataManager] Shape 로컬 로드 실패: {e.Message}"); }
    }

    private void LoadGridsFromJson()
    {
        try
        {
            var json = File.ReadAllText(GridFilePath);
            var col = JsonUtility.FromJson<BlockGridEntryCollection>(json);
            if (col?.grids == null) return;
            _gridById.Clear();
            foreach (var g in col.grids)
            {
                if (string.IsNullOrEmpty(g.grid_id)) continue;
                if (!_gridById.TryGetValue(g.grid_id, out var list))
                {
                    list = new List<BlockGridEntry>();
                    _gridById[g.grid_id] = list;
                }
                list.Add(g);
            }
        }
        catch (Exception e) { Debug.LogError($"[BlockDataManager] Grid 로컬 로드 실패: {e.Message}"); }
    }

    private void SaveShapesToJson()
    {
        var col = new BlockShapeEntryCollection { shapes = new List<BlockShapeEntry>(_shapeById.Values) };
        File.WriteAllText(ShapeFilePath, JsonUtility.ToJson(col, true));
    }

    private void SaveGridsToJson()
    {
        var all = new List<BlockGridEntry>();
        foreach (var list in _gridById.Values) all.AddRange(list);
        var col = new BlockGridEntryCollection { grids = all };
        File.WriteAllText(GridFilePath, JsonUtility.ToJson(col, true));
    }

    // ── 서버 로드 ──────────────────────────────

    private async UniTask LoadFromServerAsync()
    {
        var tableResult = Backend.CDN.Content.Table.Get();
        if (!tableResult.IsSuccess()) return;

        var contentResult = Backend.CDN.Content.Get(tableResult.GetContentTableItemList());
        if (!contentResult.IsSuccess()) return;

        Backend.CDN.Content.Local.Save(contentResult.GetContentList(), out _);
        var localResult = Backend.CDN.Content.Local.Load();
        if (!localResult.IsSuccess()) return;

        var dic = localResult.GetContentDictionarySortByChartId();

        // Shape 데이터
        if (dic.ContainsKey(ShapeChartId))
        {
            var json = JsonMapper.ToObject(dic[ShapeChartId].contentJson.ToString());
            int count = 0;
            foreach (JsonData row in json)
            {
                var entry = ParseShapeRow(row);
                if (entry == null) continue;
                if (_shapeById.TryGetValue(entry.shape_id, out var existing) &&
                    entry.stat_version <= existing.stat_version)
                    continue;
                _shapeById[entry.shape_id] = entry;
                count++;
            }
            SaveShapesToJson();
            Debug.Log($"[BlockDataManager] Shape {count}개 갱신");
        }

        // Grid 데이터
        if (dic.ContainsKey(GridChartId))
        {
            var json = JsonMapper.ToObject(dic[GridChartId].contentJson.ToString());
            int count = 0;
            foreach (JsonData row in json)
            {
                var entry = ParseGridRow(row);
                if (entry == null || string.IsNullOrEmpty(entry.grid_id)) continue;

                if (!_gridById.TryGetValue(entry.grid_id, out var list))
                {
                    list = new List<BlockGridEntry>();
                    _gridById[entry.grid_id] = list;
                }
                list.RemoveAll(g => g.slot == entry.slot);
                list.Add(entry);
                count++;
            }
            SaveGridsToJson();
            Debug.Log($"[BlockDataManager] Grid {count}행 갱신");
        }

        await UniTask.CompletedTask;
    }

    private static BlockShapeEntry ParseShapeRow(JsonData row)
    {
        try
        {
            return new BlockShapeEntry
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

    private static BlockGridEntry ParseGridRow(JsonData row)
    {
        try
        {
            return new BlockGridEntry
            {
                grid_id      = row.TryGetString("grid_id"),
                grid_name    = row.TryGetString("grid_name"),
                order        = row.TryGetInt("order"),
                rows         = row.TryGetInt("rows"),
                cols         = row.TryGetInt("cols"),
                g1           = row.TryGetString("g1"),
                g2           = row.TryGetString("g2"),
                g3           = row.TryGetString("g3"),
                g4           = row.TryGetString("g4"),
                g5           = row.TryGetString("g5"),
                g6           = row.TryGetString("g6"),
                g7           = row.TryGetString("g7"),
                g8           = row.TryGetString("g8"),
                slot         = row.TryGetInt("slot"),
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
}
