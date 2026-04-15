using BackEnd;
using UnityEngine;
using System.Collections.Generic;
using System.IO;
using Cysharp.Threading.Tasks;
using System;
using LitJson;

/// <summary>
/// 뒤끝 CDN 차트에서 맵 데이터를 로드/캐시.
/// MonsterDataManager와 동일 패턴.
/// </summary>
public class MapDataManager
{
    private const string DataFileName = "map_data.json";
    private string FilePath => Path.Combine(Application.persistentDataPath, DataFileName);

    /// <summary>뒤끝 콘솔에서 생성한 MapData 차트 ID.</summary>
    private const string ChartId = "234787";

    private Dictionary<string, MapRoomEntry> _byId = new();
    private Dictionary<string, List<MapRoomEntry>> _byCategory = new();

    public bool IsInitialized { get; private set; }

    // ── 초기화 ──────────────────────────────────────

    public async UniTask InitializeAsync()
    {
        if (File.Exists(FilePath))
        {
            LoadFromJson();
            await LoadFromServerAsync();
        }
        else
        {
            await LoadFromServerAsync();
        }

        IsInitialized = true;
        Debug.Log($"[MapDataManager] 초기화 완료. 방 {_byId.Count}개 로드됨.");
    }

    /// <summary>오프라인/테스트용: JSON 문자열로 직접 초기화.</summary>
    public void InitializeFromJson(string json)
    {
        _byId.Clear();
        _byCategory.Clear();

        var collection = JsonUtility.FromJson<MapRoomEntryCollection>(json);
        if (collection?.rooms == null) return;

        foreach (var room in collection.rooms)
            Register(room);

        IsInitialized = true;
        Debug.Log($"[MapDataManager] JSON 직접 로드. 방 {_byId.Count}개.");
    }

    // ── 조회 ──────────────────────────────────────

    public MapRoomEntry GetById(string roomId)
    {
        if (_byId.TryGetValue(roomId, out var entry))
            return entry;

        // room_id로 못 찾으면 room_id가 prefabKey와 다를 수 있으므로 전체 탐색
        foreach (var kv in _byId)
            if (kv.Value.room_id == roomId)
                return kv.Value;

        return null;
    }

    public List<MapRoomEntry> GetByCategory(string category)
    {
        _byCategory.TryGetValue(category, out var list);
        return list;
    }

    public IReadOnlyDictionary<string, MapRoomEntry> GetAll() => _byId;

    /// <summary>grid_csv를 파싱하여 TileType 그리드 반환. csv 없으면 null.</summary>
    public TileType[,] GetGrid(string roomId)
    {
        var entry = GetById(roomId);
        if (entry == null || string.IsNullOrWhiteSpace(entry.grid_csv))
            return null;

        return MapDataLoader.Parse(entry.grid_csv);
    }

    /// <summary>해당 방이 자동 생성(rule) 모드인지 여부.</summary>
    public bool IsRuleMode(string roomId)
    {
        var entry = GetById(roomId);
        return entry != null && string.IsNullOrWhiteSpace(entry.grid_csv);
    }

    // ── 내부: 등록 ──────────────────────────────────

    private void Register(MapRoomEntry entry)
    {
        if (string.IsNullOrEmpty(entry.room_id)) return;

        _byId[entry.room_id] = entry;

        if (!string.IsNullOrEmpty(entry.category))
        {
            if (!_byCategory.TryGetValue(entry.category, out var list))
            {
                list = new List<MapRoomEntry>();
                _byCategory[entry.category] = list;
            }
            // 중복 방지
            list.RemoveAll(r => r.room_id == entry.room_id);
            list.Add(entry);
        }
    }

    // ── 내부: 로컬 저장/로드 ────────────────────────

    private void LoadFromJson()
    {
        try
        {
            string json = File.ReadAllText(FilePath);
            var collection = JsonUtility.FromJson<MapRoomEntryCollection>(json);
            if (collection?.rooms == null) return;

            _byId.Clear();
            _byCategory.Clear();
            foreach (var room in collection.rooms)
                Register(room);

            Debug.Log($"[MapDataManager] 로컬 로드: {_byId.Count}개");
        }
        catch (Exception e)
        {
            Debug.LogError($"[MapDataManager] 로컬 JSON 로드 실패: {e.Message}");
        }
    }

    private void SaveToJson()
    {
        try
        {
            var list = new List<MapRoomEntry>(_byId.Values);
            var collection = new MapRoomEntryCollection { rooms = list };
            string json = JsonUtility.ToJson(collection, true);
            File.WriteAllText(FilePath, json);
            Debug.Log($"[MapDataManager] 로컬 저장: {list.Count}개 at {FilePath}");
        }
        catch (Exception e)
        {
            Debug.LogError($"[MapDataManager] 로컬 JSON 저장 실패: {e.Message}");
        }
    }

    // ── 내부: 뒤끝 CDN 로드 ─────────────────────────

    private async UniTask LoadFromServerAsync()
    {
        int loaded = ChartLoader.Load("STAGE_DATA", row =>
        {
            var entry = ParseRow(row);
            if (entry == null) return;

            if (_byId.TryGetValue(entry.room_id, out var existing))
            {
                if (entry.stat_version <= existing.stat_version)
                    return;
            }

            Register(entry);
        });

        if (loaded > 0) SaveToJson();

        await UniTask.CompletedTask;
    }

    private static MapRoomEntry ParseRow(JsonData row)
    {
        try
        {
            var entry = new MapRoomEntry();
            entry.room_id        = row.TryGetString("room_id");
            entry.category       = row.TryGetString("category");
            entry.theme          = row.TryGetString("theme");
            entry.palette        = row.TryGetString("palette");
            entry.width          = row.TryGetInt("width");
            entry.height         = row.TryGetInt("height");
            entry.grid_csv       = row.TryGetString("grid_csv");
            entry.layout_rule    = row.TryGetString("layout_rule");
            entry.entrance       = row.TryGetString("entrance");
            entry.scatter_range  = row.TryGetFloat("scatter_range");
            entry.return_duration= row.TryGetFloat("return_duration");
            entry.stat_version   = row.TryGetInt("stat_version");
            return entry;
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[MapDataManager] 행 파싱 실패: {e.Message}");
            return null;
        }
    }
}

// ── LitJson 확장 (안전한 파싱 헬퍼) ────────────────

public static class JsonDataExtensions
{
    public static string TryGetString(this JsonData data, string key)
    {
        if (data == null || !data.Keys.Contains(key) || data[key] == null) return "";
        return data[key].ToString();
    }

    public static int TryGetInt(this JsonData data, string key)
    {
        var s = data.TryGetString(key);
        int.TryParse(s, out int v);
        return v;
    }

    public static float TryGetFloat(this JsonData data, string key)
    {
        var s = data.TryGetString(key);
        float.TryParse(s, System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out float v);
        return v;
    }
}
