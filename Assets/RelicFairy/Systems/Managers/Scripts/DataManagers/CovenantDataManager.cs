using System;
using System.Collections.Generic;
using System.IO;
using Cysharp.Threading.Tasks;
using LitJson;
using UnityEngine;

/// <summary>
/// 뒤끝 CDN에서 COVENANT_STAT_DATA 차트를 로드한다.
/// covenant_id + slot 조합으로 스테이지별 float 수치를 제공한다.
///
/// CSV 컬럼: covenant_id | slot | basic | enhanced | evolved | stat_version
/// </summary>
public sealed class CovenantDataManager
{
    private const string ChartName    = "COVENANT_STAT_DATA";
    private const string DataFileName = "covenant_stat_data.json";

    private string FilePath => Path.Combine(Application.persistentDataPath, DataFileName);

    // covenant_id → (slot → entry)
    private readonly Dictionary<string, Dictionary<int, CovenantStatEntry>> _map = new();

    public bool IsInitialized { get; private set; }

    // ── 초기화 ──────────────────────────────────────────────────────────────

    public async UniTask InitializeAsync()
    {
        if (File.Exists(FilePath)) LoadFromJson();

        try { await LoadFromServerAsync(); }
        catch (Exception e) { Debug.LogWarning($"[CovenantDataManager] CDN 예외: {e.Message}"); }

        if (_map.Count == 0)
        {
            Debug.Log("[CovenantDataManager] CDN 실패 — Addressables 폴백");
            var textAsset = await Managers.AddressableManager.TryLoadAssetAsync<TextAsset>(ChartName);
            if (textAsset != null)
            {
                var col = JsonUtility.FromJson<CovenantStatEntryCollection>(textAsset.text);
                if (col?.entries != null)
                    foreach (var e in col.entries) Register(e);
            }
        }

        IsInitialized = true;
        Debug.Log($"[CovenantDataManager] 초기화 완료. {_map.Count}개 서약 로드");
    }

    // ── 조회 ────────────────────────────────────────────────────────────────

    public bool HasData(string covenantId) => _map.ContainsKey(covenantId);

    /// <summary>서약 ID + 스테이지 + 슬롯 인덱스로 float 수치 반환. 없으면 fallback.</summary>
    public float Get(string covenantId, CovenantStage stage, int slot, float fallback = 0f)
    {
        if (_map.TryGetValue(covenantId, out var slots) &&
            slots.TryGetValue(slot, out var entry))
            return entry.Get(stage);

        return fallback;
    }

    /// <summary>Get의 int 버전 (RoundToInt).</summary>
    public int GetInt(string covenantId, CovenantStage stage, int slot, int fallback = 0)
        => Mathf.RoundToInt(Get(covenantId, stage, slot, fallback));

    // ── 내부 ────────────────────────────────────────────────────────────────

    private void Register(CovenantStatEntry entry)
    {
        if (entry == null || string.IsNullOrEmpty(entry.covenant_id)) return;

        if (!_map.TryGetValue(entry.covenant_id, out var slots))
        {
            slots = new Dictionary<int, CovenantStatEntry>();
            _map[entry.covenant_id] = slots;
        }
        slots[entry.slot] = entry;
    }

    private void LoadFromJson()
    {
        try
        {
            var json = File.ReadAllText(FilePath);
            var col  = JsonUtility.FromJson<CovenantStatEntryCollection>(json);
            if (col?.entries == null) return;

            _map.Clear();
            foreach (var e in col.entries) Register(e);
        }
        catch (Exception e) { Debug.LogError($"[CovenantDataManager] 로컬 로드 실패: {e.Message}"); }
    }

    private void SaveToJson()
    {
        var all = new List<CovenantStatEntry>();
        foreach (var slots in _map.Values)
            all.AddRange(slots.Values);

        var col = new CovenantStatEntryCollection { entries = all };
        File.WriteAllText(FilePath, JsonUtility.ToJson(col, true));
    }

    private async UniTask LoadFromServerAsync()
    {
        _map.Clear();

        int loaded = ChartLoader.Load(ChartName, row =>
        {
            var entry = ParseRow(row);
            if (entry != null) Register(entry);
        });

        if (loaded > 0) SaveToJson();

        await UniTask.CompletedTask;
    }

    private static CovenantStatEntry ParseRow(JsonData row)
    {
        try
        {
            var id = row.TryGetString("covenant_id");
            if (string.IsNullOrEmpty(id)) return null;

            return new CovenantStatEntry
            {
                index        = row.TryGetInt("index"),
                covenant_id  = id,
                slot         = row.TryGetInt("slot"),
                description  = row.TryGetString("description"),
                basic        = row.TryGetFloat("basic"),
                enhanced     = row.TryGetFloat("enhanced"),
                evolved      = row.TryGetFloat("evolved"),
                stat_version = row.TryGetInt("stat_version"),
            };
        }
        catch { return null; }
    }
}
