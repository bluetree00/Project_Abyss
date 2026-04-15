using BackEnd;
using UnityEngine;
using System.Collections.Generic;
using System.IO;
using Cysharp.Threading.Tasks;
using System;
using LitJson;

/// <summary>
/// 뒤끝 CDN에서 ITEM_DATA 로드.
/// PlayerDataManager와 동일 패턴.
/// </summary>
public class ItemDataManager
{
    private const string DataFileName = "item_data.json";
    private string FilePath => Path.Combine(Application.persistentDataPath, DataFileName);

    private const string ItemChartId = "236201";

    private Dictionary<string, List<ItemEntry>> _itemById = new();

    public bool IsInitialized { get; private set; }

    // ── 초기화 ──────────────────────────────────

    public async UniTask InitializeAsync()
    {
        if (File.Exists(FilePath)) LoadFromJson();

        try { await LoadFromServerAsync(); }
        catch (Exception e) { Debug.LogWarning($"[ItemDataManager] CDN 예외: {e.Message}"); }

        if (_itemById.Count == 0)
        {
            Debug.Log("[ItemDataManager] CDN 실패 — Resources 폴백");
            var json = Resources.Load<TextAsset>("ITEM_DATA");
            if (json != null)
            {
                var col = JsonUtility.FromJson<ItemEntryCollection>(json.text);
                if (col?.items != null)
                    foreach (var entry in col.items)
                    {
                        // 새 CSV 호환
                        if (string.IsNullOrEmpty(entry.item_id) && !string.IsNullOrEmpty(entry.passive_id))
                            entry.item_id = entry.passive_id;

                        var id = entry.ResolvedId;
                        if (string.IsNullOrEmpty(id)) continue;
                        if (!_itemById.TryGetValue(id, out var list))
                        {
                            list = new List<ItemEntry>();
                            _itemById[id] = list;
                        }
                        list.Add(entry);
                    }
            }
        }

        IsInitialized = true;
        Debug.Log($"[ItemDataManager] 초기화 완료. 아이템 {_itemById.Count}종");
    }

    // ── 조회 ──────────────────────────────────

    /// <summary>아이템 ID로 모든 슬롯 엔트리 반환.</summary>
    public List<ItemEntry> GetItem(string itemId)
    {
        _itemById.TryGetValue(itemId, out var list);
        return list;
    }

    /// <summary>아이템 ID로 slot 1 엔트리만 반환 (메타 정보용).</summary>
    public ItemEntry GetItemMeta(string itemId)
    {
        if (!_itemById.TryGetValue(itemId, out var list)) return null;
        foreach (var e in list)
            if (e.slot == 1) return e;
        return list.Count > 0 ? list[0] : null;
    }

    /// <summary>전체 아이템 ID 목록.</summary>
    public IReadOnlyDictionary<string, List<ItemEntry>> GetAllItems() => _itemById;

    // ── 로컬 저장/로드 ──────────────────────────

    private void LoadFromJson()
    {
        try
        {
            var json = File.ReadAllText(FilePath);
            var col = JsonUtility.FromJson<ItemEntryCollection>(json);
            if (col?.items == null) return;
            _itemById.Clear();
            foreach (var entry in col.items)
            {
                if (string.IsNullOrEmpty(entry.item_id) && !string.IsNullOrEmpty(entry.passive_id))
                    entry.item_id = entry.passive_id;

                var id = entry.ResolvedId;
                if (string.IsNullOrEmpty(id)) continue;
                if (!_itemById.TryGetValue(id, out var list))
                {
                    list = new List<ItemEntry>();
                    _itemById[id] = list;
                }
                list.Add(entry);
            }
        }
        catch (Exception e) { Debug.LogError($"[ItemDataManager] 로컬 로드 실패: {e.Message}"); }
    }

    private void SaveToJson()
    {
        var all = new List<ItemEntry>();
        foreach (var list in _itemById.Values) all.AddRange(list);
        var col = new ItemEntryCollection { items = all };
        File.WriteAllText(FilePath, JsonUtility.ToJson(col, true));
    }

    // ── 서버 로드 ──────────────────────────────

    private async UniTask LoadFromServerAsync()
    {
        int loaded = ChartLoader.Load("ITEM_DATA", row =>
        {
            var entry = ParseRow(row);
            var id = entry?.ResolvedId;
            if (entry == null || string.IsNullOrEmpty(id)) return;

            if (!_itemById.TryGetValue(id, out var list))
            {
                list = new List<ItemEntry>();
                _itemById[id] = list;
            }
            list.RemoveAll(e => e.slot == entry.slot);
            list.Add(entry);
        });

        if (loaded > 0) SaveToJson();

        await UniTask.CompletedTask;
    }

    private static ItemEntry ParseRow(JsonData row)
    {
        try
        {
            var entry = new ItemEntry
            {
                item_id      = row.TryGetString("item_id"),
                passive_id   = row.TryGetString("passive_id"),
                item_name    = row.TryGetString("item_name"),
                rarity       = row.TryGetString("rarity"),
                category     = row.TryGetString("category"),
                slot         = row.TryGetInt("slot"),
                effect_type  = row.TryGetString("effect_type"),
                trigger      = row.TryGetString("trigger"),
                value        = row.TryGetFloat("value"),
                value2       = row.TryGetFloat("value2"),
                value3       = row.TryGetFloat("value3"),
                max_stack    = row.TryGetInt("max_stack"),
                duration     = row.TryGetFloat("duration"),
                cooldown     = row.TryGetFloat("cooldown"),
                shape_id     = row.TryGetInt("shape_id"),
                icon_key     = row.TryGetString("icon_key"),
                description  = row.TryGetString("description"),
                stat_version = row.TryGetInt("stat_version"),
            };

            // 새 CSV 호환: passive_id만 있고 item_id가 비었으면 복사
            if (string.IsNullOrEmpty(entry.item_id) && !string.IsNullOrEmpty(entry.passive_id))
                entry.item_id = entry.passive_id;

            return entry;
        }
        catch { return null; }
    }
}
