using System;
using System.Collections.Generic;
using System.IO;
using Cysharp.Threading.Tasks;
using LitJson;
using UnityEngine;

/// <summary>
/// 뒤끝 CDN에서 RELIC_PARTS_DATA 차트를 로드한다.
/// relic_id + part_kind + boss_tier로 드래프트 후보 풀을 제공한다.
///
/// 로드 우선순위: 로컬 캐시 JSON → CDN → Addressables 폴백(오프라인).
/// RelicAwakeningDataManager와 동일한 패턴.
///
/// CSV 컬럼: index | relic_id | part_kind | part_id | part_name | description | effect_key | boss_tier
/// </summary>
public sealed class RelicPartsDataManager
{
    private const string ChartName    = "RELIC_PARTS_DATA";
    private const string DataFileName = "relic_parts_data.json";

    private string FilePath => Path.Combine(Application.persistentDataPath, DataFileName);

    // part_id → entry (전체 조회)
    private readonly Dictionary<string, RelicPartEntry> _byId = new();
    // relic_id → entries (드래프트 풀 필터용)
    private readonly Dictionary<string, List<RelicPartEntry>> _byRelic = new();

    public bool IsInitialized { get; private set; }

    // ── 초기화 ──────────────────────────────────────────────────────────────

    public async UniTask InitializeAsync()
    {
        if (File.Exists(FilePath)) LoadFromJson();

        try { await LoadFromServerAsync(); }
        catch (Exception e) { Debug.LogWarning($"[RelicPartsDataManager] CDN 예외: {e.Message}"); }

        if (_byId.Count == 0)
        {
            Debug.Log("[RelicPartsDataManager] CDN 실패 — Addressables 폴백");
            var textAsset = await Managers.AddressableManager.TryLoadAssetAsync<TextAsset>(ChartName);
            if (textAsset != null)
            {
                var col = JsonUtility.FromJson<RelicPartEntryCollection>(textAsset.text);
                if (col?.entries != null)
                    foreach (var e in col.entries) Register(e);
            }
        }

        IsInitialized = true;
        Debug.Log($"[RelicPartsDataManager] 초기화 완료. {_byId.Count}개 파츠 로드");
    }

    // ── 조회 ────────────────────────────────────────────────────────────────

    /// <summary>part_id로 단일 파츠 조회. 없으면 null.</summary>
    public RelicPartEntry GetById(string partId)
    {
        if (string.IsNullOrEmpty(partId)) return null;
        return _byId.TryGetValue(partId, out var e) ? e : null;
    }

    /// <summary>
    /// 드래프트 후보 풀 — 해당 유물의 파츠 중 boss_tier가 일치하고, 이미 보유하지 않은 것.
    /// bossTier 1 = 기능 파츠(effect/behavior/trigger), 3 = 코어 진화(core). 데이터의 boss_tier로 구분한다.
    /// </summary>
    public List<RelicPartEntry> GetDraftPool(string relicId, int bossTier, IReadOnlyList<string> ownedPartIds)
    {
        var result = new List<RelicPartEntry>();
        if (string.IsNullOrEmpty(relicId) || !_byRelic.TryGetValue(relicId, out var list)) return result;

        foreach (var e in list)
        {
            if (e.boss_tier != bossTier) continue;
            if (Owns(ownedPartIds, e.part_id)) continue;
            result.Add(e);
        }
        return result;
    }

    // ── 내부 ────────────────────────────────────────────────────────────────

    private static bool Owns(IReadOnlyList<string> ownedPartIds, string partId)
    {
        if (ownedPartIds == null) return false;
        for (int i = 0; i < ownedPartIds.Count; i++)
            if (ownedPartIds[i] == partId) return true;
        return false;
    }

    private void Register(RelicPartEntry entry)
    {
        if (entry == null || string.IsNullOrEmpty(entry.part_id) || string.IsNullOrEmpty(entry.relic_id)) return;

        _byId[entry.part_id] = entry;

        if (!_byRelic.TryGetValue(entry.relic_id, out var list))
        {
            list = new List<RelicPartEntry>();
            _byRelic[entry.relic_id] = list;
        }
        list.Add(entry);
    }

    private void ClearAll()
    {
        _byId.Clear();
        _byRelic.Clear();
    }

    private void LoadFromJson()
    {
        try
        {
            var json = File.ReadAllText(FilePath);
            var col  = JsonUtility.FromJson<RelicPartEntryCollection>(json);
            if (col?.entries == null) return;

            ClearAll();
            foreach (var e in col.entries) Register(e);
        }
        catch (Exception e) { Debug.LogError($"[RelicPartsDataManager] 로컬 로드 실패: {e.Message}"); }
    }

    private void SaveToJson()
    {
        var all = new List<RelicPartEntry>(_byId.Values);
        var col = new RelicPartEntryCollection { entries = all };
        File.WriteAllText(FilePath, JsonUtility.ToJson(col, true));
    }

    private async UniTask LoadFromServerAsync()
    {
        ClearAll();

        int loaded = ChartLoader.Load(ChartName, row =>
        {
            var entry = ParseRow(row);
            if (entry != null) Register(entry);
        });

        if (loaded > 0) SaveToJson();

        await UniTask.CompletedTask;
    }

    private static RelicPartEntry ParseRow(JsonData row)
    {
        try
        {
            var partId = row.TryGetString("part_id");
            if (string.IsNullOrEmpty(partId)) return null;

            return new RelicPartEntry
            {
                index       = row.TryGetInt("index"),
                relic_id    = row.TryGetString("relic_id"),
                part_kind   = row.TryGetString("part_kind"),
                part_id     = partId,
                part_name   = row.TryGetString("part_name"),
                description = row.TryGetString("description"),
                effect_key  = row.TryGetString("effect_key"),
                boss_tier   = row.TryGetInt("boss_tier"),
            };
        }
        catch { return null; }
    }
}
