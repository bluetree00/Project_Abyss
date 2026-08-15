using System;
using System.Collections.Generic;
using System.IO;
using Cysharp.Threading.Tasks;
using LitJson;
using UnityEngine;

/// <summary>
/// 뒤끝 CDN에서 RELIC_STAT_DATA 차트를 로드한다. relic_id + slot → float 수치.
/// 표준 차트 매니저 패턴(캐시 → CDN → Addressables 폴백).
///
/// CSV 컬럼: index | relic_id | slot | description | value | stat_version
/// </summary>
public sealed class RelicStatDataManager
{
    private const string ChartName    = "RELIC_STAT_DATA";
    private const string DataFileName = "relic_stat_data.json";

    private string FilePath => Path.Combine(Application.persistentDataPath, DataFileName);

    // relic_id → (slot → entry)
    private readonly Dictionary<string, Dictionary<int, RelicStatEntry>> _map = new();

    public bool IsInitialized { get; private set; }

    public async UniTask InitializeAsync()
    {
        if (File.Exists(FilePath)) LoadFromJson();

        try { await LoadFromServerAsync(); }
        catch (Exception e) { Debug.LogWarning($"[RelicStatDataManager] CDN 예외: {e.Message}"); }

        if (_map.Count == 0)
        {
            var textAsset = await Managers.AddressableManager.TryLoadAssetAsync<TextAsset>(ChartName);
            if (textAsset != null)
            {
                var col = JsonUtility.FromJson<RelicStatEntryCollection>(textAsset.text);
                if (col?.entries != null) foreach (var e in col.entries) Register(e);
            }
        }

        IsInitialized = true;
        Debug.Log($"[RelicStatDataManager] 초기화 완료. {_map.Count}개 유물 로드");
    }

    public bool HasData(string relicId) => _map.ContainsKey(relicId);

    /// <summary>relic_id + slot → float. 없으면 fallback.</summary>
    public float Get(string relicId, int slot, float fallback = 0f)
    {
        if (_map.TryGetValue(relicId, out var slots) && slots.TryGetValue(slot, out var entry))
            return entry.value;
        return fallback;
    }

    public int GetInt(string relicId, int slot, int fallback = 0) => Mathf.RoundToInt(Get(relicId, slot, fallback));

    private void Register(RelicStatEntry e)
    {
        if (e == null || string.IsNullOrEmpty(e.relic_id)) return;
        if (!_map.TryGetValue(e.relic_id, out var slots))
        {
            slots = new Dictionary<int, RelicStatEntry>();
            _map[e.relic_id] = slots;
        }
        slots[e.slot] = e;
    }

    private void LoadFromJson()
    {
        try
        {
            var col = JsonUtility.FromJson<RelicStatEntryCollection>(File.ReadAllText(FilePath));
            if (col?.entries == null) return;
            _map.Clear();
            foreach (var e in col.entries) Register(e);
        }
        catch (Exception e) { Debug.LogError($"[RelicStatDataManager] 로컬 로드 실패: {e.Message}"); }
    }

    private void SaveToJson()
    {
        var all = new List<RelicStatEntry>();
        foreach (var slots in _map.Values) all.AddRange(slots.Values);
        File.WriteAllText(FilePath, JsonUtility.ToJson(new RelicStatEntryCollection { entries = all }, true));
    }

    private async UniTask LoadFromServerAsync()
    {
        _map.Clear();
        int loaded = ChartLoader.Load(ChartName, row => { var e = ParseRow(row); if (e != null) Register(e); });
        if (loaded > 0) SaveToJson();
        await UniTask.CompletedTask;
    }

    private static RelicStatEntry ParseRow(JsonData row)
    {
        try
        {
            var id = row.TryGetString("relic_id");
            if (string.IsNullOrEmpty(id)) return null;
            return new RelicStatEntry
            {
                index        = row.TryGetInt("index"),
                relic_id     = id,
                slot         = row.TryGetInt("slot"),
                description  = row.TryGetString("description"),
                value        = row.TryGetFloat("value"),
                stat_version = row.TryGetInt("stat_version"),
            };
        }
        catch { return null; }
    }
}
