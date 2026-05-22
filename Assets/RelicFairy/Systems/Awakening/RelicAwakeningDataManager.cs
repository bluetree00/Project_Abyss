using System;
using System.Collections.Generic;
using System.IO;
using Cysharp.Threading.Tasks;
using LitJson;
using UnityEngine;

/// <summary>
/// 뒤끝 CDN에서 RELIC_AWAKENING_DATA 차트를 로드한다.
/// category_id + level 복합 키로 각성 노드 목록을 제공한다.
///
/// CSV 컬럼: index | category_id | level | stat_type | value | cost | description
/// </summary>
public sealed class RelicAwakeningDataManager
{
    private const string ChartName    = "RELIC_AWAKENING_DATA";
    private const string DataFileName = "relic_awakening_data.json";

    private string FilePath => Path.Combine(Application.persistentDataPath, DataFileName);

    // category_id → (level → entries) — 한 레벨에 보조 행이 있을 수 있으므로 List
    private readonly Dictionary<string, Dictionary<int, List<RelicAwakeningEntry>>> _map = new();

    public bool IsInitialized { get; private set; }

    // ── 초기화 ──────────────────────────────────────────────────────────────

    public async UniTask InitializeAsync()
    {
        if (File.Exists(FilePath)) LoadFromJson();

        try { await LoadFromServerAsync(); }
        catch (Exception e) { Debug.LogWarning($"[RelicAwakeningDataManager] CDN 예외: {e.Message}"); }

        if (_map.Count == 0)
        {
            Debug.Log("[RelicAwakeningDataManager] CDN 실패 — Addressables 폴백");
            var textAsset = await Managers.AddressableManager.TryLoadAssetAsync<TextAsset>(ChartName);
            if (textAsset != null)
            {
                var col = JsonUtility.FromJson<RelicAwakeningEntryCollection>(textAsset.text);
                if (col?.entries != null)
                    foreach (var e in col.entries) Register(e);
            }
        }

        IsInitialized = true;
        int total = 0;
        foreach (var cat in _map.Values)
            foreach (var lvl in cat.Values)
                total += lvl.Count;
        Debug.Log($"[RelicAwakeningDataManager] 초기화 완료. {total}개 노드 로드");
    }

    // ── 조회 ────────────────────────────────────────────────────────────────

    /// <summary>카테고리의 최대 레벨 수를 반환한다.</summary>
    public int GetMaxLevel(string categoryId)
    {
        if (!_map.TryGetValue(categoryId, out var levels)) return 0;
        int max = 0;
        foreach (var lv in levels.Keys)
            if (lv > max) max = lv;
        return max;
    }

    /// <summary>특정 카테고리 + 레벨의 모든 엔트리를 반환한다 (보조 행 포함).</summary>
    public IReadOnlyList<RelicAwakeningEntry> GetEntries(string categoryId, int level)
    {
        if (_map.TryGetValue(categoryId, out var levels) &&
            levels.TryGetValue(level, out var entries))
            return entries;
        return System.Array.Empty<RelicAwakeningEntry>();
    }

    /// <summary>1~currentLevel 모든 엔트리 누적 반환.</summary>
    public List<RelicAwakeningEntry> GetEntriesUpToLevel(string categoryId, int currentLevel)
    {
        var result = new List<RelicAwakeningEntry>();
        if (!_map.TryGetValue(categoryId, out var levels)) return result;

        for (int lv = 1; lv <= currentLevel; lv++)
        {
            if (levels.TryGetValue(lv, out var entries))
                result.AddRange(entries);
        }
        return result;
    }

    /// <summary>다음 레벨 달성 비용 (currentLevel+1 의 cost 합산).</summary>
    public int GetUpgradeCost(string categoryId, int currentLevel)
    {
        int nextLevel = currentLevel + 1;
        var entries = GetEntries(categoryId, nextLevel);
        int total = 0;
        foreach (var e in entries) total += e.cost;
        return total;
    }

    // ── 내부 ────────────────────────────────────────────────────────────────

    private void Register(RelicAwakeningEntry entry)
    {
        if (entry == null || string.IsNullOrEmpty(entry.category_id) || entry.level <= 0) return;

        if (!_map.TryGetValue(entry.category_id, out var levels))
        {
            levels = new Dictionary<int, List<RelicAwakeningEntry>>();
            _map[entry.category_id] = levels;
        }

        if (!levels.TryGetValue(entry.level, out var list))
        {
            list = new List<RelicAwakeningEntry>();
            levels[entry.level] = list;
        }

        list.Add(entry);
    }

    private void LoadFromJson()
    {
        try
        {
            var json = File.ReadAllText(FilePath);
            var col  = JsonUtility.FromJson<RelicAwakeningEntryCollection>(json);
            if (col?.entries == null) return;

            _map.Clear();
            foreach (var e in col.entries) Register(e);
        }
        catch (Exception e) { Debug.LogError($"[RelicAwakeningDataManager] 로컬 로드 실패: {e.Message}"); }
    }

    private void SaveToJson()
    {
        var all = new List<RelicAwakeningEntry>();
        foreach (var levels in _map.Values)
            foreach (var list in levels.Values)
                all.AddRange(list);

        var col = new RelicAwakeningEntryCollection { entries = all };
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

    private static RelicAwakeningEntry ParseRow(JsonData row)
    {
        try
        {
            var cat = row.TryGetString("category_id");
            if (string.IsNullOrEmpty(cat)) return null;

            return new RelicAwakeningEntry
            {
                index       = row.TryGetInt("index"),
                category_id = cat,
                level       = row.TryGetInt("level"),
                stat_type   = row.TryGetString("stat_type"),
                value       = row.TryGetFloat("value"),
                cost        = row.TryGetInt("cost"),
                description = row.TryGetString("description"),
            };
        }
        catch { return null; }
    }
}
