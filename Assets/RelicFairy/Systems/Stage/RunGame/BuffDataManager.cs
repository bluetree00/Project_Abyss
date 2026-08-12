using BackEnd;
using UnityEngine;
using System.Collections.Generic;
using System.IO;
using Cysharp.Threading.Tasks;
using System;
using LitJson;

/// <summary>
/// 뒤끝 CDN에서 BUFF_DATA 로드.
/// buff_type + tier로 조회 가능.
/// </summary>
public class BuffDataManager
{
    private const string DataFileName = "buff_data.json";
    private string FilePath => Path.Combine(Application.persistentDataPath, DataFileName);

    private const string BuffChartId = "236207";

    // buff_type → tier → BuffEntry
    private readonly Dictionary<string, Dictionary<int, BuffEntry>> _byTypeTier = new();
    // buff_type → is_debuff별 엔트리 리스트 (롤링용)
    private readonly List<BuffEntry> _allBuffs = new();
    private readonly List<BuffEntry> _allDebuffs = new();
    // buff_type 목록 (버프/디버프 별)
    private readonly List<string> _buffTypes = new();
    private readonly List<string> _debuffTypes = new();

    public bool IsInitialized { get; private set; }

    // ── 초기화 ──────────────────────────────────

    public async UniTask InitializeAsync()
    {
        if (File.Exists(FilePath)) LoadFromJson();

        try { await LoadFromServerAsync(); }
        catch (Exception e) { Debug.LogWarning($"[BuffDataManager] CDN 예외: {e.Message}"); }

        if (_byTypeTier.Count == 0)
        {
            Debug.Log("[BuffDataManager] CDN 실패 — 오프라인 폴백");
            // 폴백 실물은 Resources/BUFF_DATA.json (Addressable 미등록) — Resources도 함께 본다.
            var json = await Managers.AddressableManager.TryLoadAssetAsync<TextAsset>("BUFF_DATA")
                       ?? Resources.Load<TextAsset>("BUFF_DATA");
            if (json != null)
            {
                var col = JsonUtility.FromJson<BuffEntryCollection>(json.text);
                if (col?.buffs != null)
                    foreach (var entry in col.buffs)
                        AddEntry(entry);
            }
        }

        BuildTypeLists();
        IsInitialized = true;
        Debug.Log($"[BuffDataManager] 초기화 완료. 버프 {_allBuffs.Count}개, 디버프 {_allDebuffs.Count}개");
    }

    // ── 조회 ──────────────────────────────────

    /// <summary>buff_type + tier로 엔트리 조회.</summary>
    public BuffEntry Get(string buffType, int tier)
    {
        if (_byTypeTier.TryGetValue(buffType, out var tierMap))
            if (tierMap.TryGetValue(tier, out var entry))
                return entry;
        return null;
    }

    /// <summary>랜덤 버프 타입 반환.</summary>
    public string GetRandomBuffType()
    {
        if (_buffTypes.Count == 0) return "AttackPower";
        return _buffTypes[UnityEngine.Random.Range(0, _buffTypes.Count)];
    }

    /// <summary>랜덤 디버프 타입 반환.</summary>
    public string GetRandomDebuffType()
    {
        if (_debuffTypes.Count == 0) return "AttackPower";
        return _debuffTypes[UnityEngine.Random.Range(0, _debuffTypes.Count)];
    }

    /// <summary>해당 buff_type의 최대 티어.</summary>
    public int GetMaxTier(string buffType)
    {
        if (!_byTypeTier.TryGetValue(buffType, out var tierMap)) return 1;
        int max = 1;
        foreach (var kv in tierMap)
            if (kv.Key > max) max = kv.Key;
        return max;
    }

    // ── 내부 ──────────────────────────────────

    private void AddEntry(BuffEntry entry)
    {
        if (entry == null || string.IsNullOrEmpty(entry.buff_type)) return;

        // is_debuff 구분 키: "buff_type:debuff" or "buff_type:buff"
        string key = entry.buff_type;

        if (!_byTypeTier.TryGetValue(key, out var tierMap))
        {
            tierMap = new Dictionary<int, BuffEntry>();
            _byTypeTier[key] = tierMap;
        }
        tierMap[entry.tier] = entry;

        if (entry.is_debuff)
            _allDebuffs.Add(entry);
        else
            _allBuffs.Add(entry);
    }

    private void BuildTypeLists()
    {
        var buffSet = new HashSet<string>();
        var debuffSet = new HashSet<string>();

        foreach (var e in _allBuffs)
            buffSet.Add(e.buff_type);
        foreach (var e in _allDebuffs)
            debuffSet.Add(e.buff_type);

        _buffTypes.Clear();
        _buffTypes.AddRange(buffSet);
        _debuffTypes.Clear();
        _debuffTypes.AddRange(debuffSet);
    }

    private void LoadFromJson()
    {
        try
        {
            var json = File.ReadAllText(FilePath);
            var col = JsonUtility.FromJson<BuffEntryCollection>(json);
            if (col?.buffs == null) return;
            _byTypeTier.Clear();
            _allBuffs.Clear();
            _allDebuffs.Clear();
            foreach (var entry in col.buffs)
                AddEntry(entry);
        }
        catch (Exception e) { Debug.LogError($"[BuffDataManager] 로컬 로드 실패: {e.Message}"); }
    }

    private void SaveToJson()
    {
        var all = new List<BuffEntry>();
        all.AddRange(_allBuffs);
        all.AddRange(_allDebuffs);
        var col = new BuffEntryCollection { buffs = all };
        File.WriteAllText(FilePath, JsonUtility.ToJson(col, true));
    }

    private async UniTask LoadFromServerAsync()
    {
        _byTypeTier.Clear();
        _allBuffs.Clear();
        _allDebuffs.Clear();

        int loaded = ChartLoader.Load("BUFF_DATA", row =>
        {
            var entry = ParseRow(row);
            if (entry != null) AddEntry(entry);
        });

        if (loaded > 0) SaveToJson();

        await UniTask.CompletedTask;
    }

    private static BuffEntry ParseRow(JsonData row)
    {
        try
        {
            return new BuffEntry
            {
                buff_id      = row.TryGetString("buff_id"),
                buff_type    = row.TryGetString("buff_type"),
                stat_type    = row.TryGetString("stat_type"),
                is_debuff    = row.TryGetString("is_debuff") == "true",
                tier         = row.TryGetInt("tier"),
                value        = row.TryGetFloat("value"),
                is_percent   = row.TryGetString("is_percent") == "true",
                description  = row.TryGetString("description"),
                stat_version = row.TryGetInt("stat_version"),
            };
        }
        catch { return null; }
    }
}
