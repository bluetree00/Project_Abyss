using System;
using System.Collections.Generic;
using System.IO;
using Cysharp.Threading.Tasks;
using LitJson;
using UnityEngine;

/// <summary>
/// 뒤끝 CDN에서 WEAPON_PARTS_DATA 차트를 로드한다 — 원거리 파츠의 정적 정의.
///
/// 로드 우선순위: 로컬 캐시 JSON → CDN → Addressables 폴백(오프라인).
/// <see cref="RelicPartsDataManager"/>와 동일한 패턴이라 운용 방식(차트 갱신·캐시 정리)이 같다.
///
/// CSV 컬럼: index | part_id | part_name | description | kind | base_value | per_level | milestone_every | max_level
/// </summary>
public sealed class WeaponPartsDataManager
{
    private const string ChartName    = "WEAPON_PARTS_DATA";
    private const string DataFileName = "weapon_parts_data.json";

    private static readonly HashSet<string> ValidKinds = new()
    { "split", "pierce", "explode", "homing", "power" };

    private string FilePath => Path.Combine(Application.persistentDataPath, DataFileName);

    private readonly Dictionary<string, WeaponPartEntry> _byId = new();
    private readonly List<WeaponPartEntry>               _all  = new();

    public bool IsInitialized { get; private set; }

    /// <summary>정의된 전 파츠(획득 풀·UI 목록용).</summary>
    public IReadOnlyList<WeaponPartEntry> All => _all;

    // ── 초기화 ──────────────────────────────────────────────

    public async UniTask InitializeAsync()
    {
        if (File.Exists(FilePath)) LoadFromJson();

        try { await LoadFromServerAsync(); }
        catch (Exception e) { Debug.LogWarning($"[WeaponPartsDataManager] CDN 예외: {e.Message}"); }

        if (_byId.Count == 0)
        {
            Debug.Log("[WeaponPartsDataManager] CDN 실패 — Addressables 폴백");
            var textAsset = await Managers.AddressableManager.TryLoadAssetAsync<TextAsset>(ChartName);
            if (textAsset != null)
            {
                var col = JsonUtility.FromJson<WeaponPartEntryCollection>(textAsset.text);
                if (col?.entries != null)
                    foreach (var e in col.entries) Register(e);
            }
        }

        IsInitialized = true;
        Debug.Log($"[WeaponPartsDataManager] 초기화 완료. {_byId.Count}개 원거리 파츠 로드");
    }

    // ── 조회 ────────────────────────────────────────────────

    /// <summary>part_id로 단일 파츠 정의. 없으면 null.</summary>
    public WeaponPartEntry GetById(string partId)
    {
        if (string.IsNullOrEmpty(partId)) return null;
        return _byId.TryGetValue(partId, out var e) ? e : null;
    }

    // ── 내부 ────────────────────────────────────────────────

    private void Register(WeaponPartEntry entry)
    {
        if (entry == null || string.IsNullOrEmpty(entry.part_id)) return;

        // kind 오타는 조용히 Split로 폴백되므로(WeaponPartEntry.Kind) 여기서 경고를 남긴다.
        if (!ValidKinds.Contains(entry.kind))
            Debug.LogWarning($"[WeaponPartsDataManager] 알 수 없는 kind '{entry.kind}' (part_id={entry.part_id}) — split로 처리됨");

        if (!_byId.ContainsKey(entry.part_id)) _all.Add(entry);
        _byId[entry.part_id] = entry;
    }

    private void ClearAll()
    {
        _byId.Clear();
        _all.Clear();
    }

    private void LoadFromJson()
    {
        try
        {
            var json = File.ReadAllText(FilePath);
            var col  = JsonUtility.FromJson<WeaponPartEntryCollection>(json);
            if (col?.entries == null) return;

            ClearAll();
            foreach (var e in col.entries) Register(e);
        }
        catch (Exception e) { Debug.LogError($"[WeaponPartsDataManager] 로컬 로드 실패: {e.Message}"); }
    }

    private void SaveToJson()
    {
        var col = new WeaponPartEntryCollection { entries = new List<WeaponPartEntry>(_all) };
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

    private static WeaponPartEntry ParseRow(JsonData row)
    {
        try
        {
            var partId = row.TryGetString("part_id");
            if (string.IsNullOrEmpty(partId)) return null;

            return new WeaponPartEntry
            {
                index           = row.TryGetInt("index"),
                part_id         = partId,
                part_name       = row.TryGetString("part_name"),
                description     = row.TryGetString("description"),
                kind            = row.TryGetString("kind"),
                base_value      = row.TryGetFloat("base_value"),
                per_level       = row.TryGetFloat("per_level"),
                milestone_every = row.TryGetInt("milestone_every"),
                max_level       = row.TryGetInt("max_level"),
            };
        }
        catch { return null; }
    }
}
