using UnityEngine;
using System.Collections.Generic;
using System.IO;
using Cysharp.Threading.Tasks;
using System;
using LitJson;

/// <summary>
/// 뒤끝 CDN에서 CHAPTER_DATA 로드.
/// chapter_id로 조회. stat_version 기반 패치 관리.
/// </summary>
public class ChapterDataManager
{
    private const string DataFileName = "chapter_data.json";
    private string FilePath => Path.Combine(Application.persistentDataPath, DataFileName);

    private readonly Dictionary<string, ChapterServerEntry> _byId = new();

    public bool IsInitialized { get; private set; }

    // ── 초기화 ──

    public async UniTask InitializeAsync()
    {
        if (File.Exists(FilePath)) LoadFromJson();

        try { await LoadFromServerAsync(); }
        catch (Exception e) { Debug.LogWarning($"[ChapterDataManager] CDN 예외: {e.Message}"); }

        if (_byId.Count == 0)
        {
            Debug.Log("[ChapterDataManager] CDN 실패 — Addressables 폴백");
            var json = await Managers.AddressableManager.TryLoadAssetAsync<TextAsset>("CHAPTER_DATA");
            if (json != null) InitializeFromJson(json.text);
        }

        IsInitialized = true;
        Debug.Log($"[ChapterDataManager] 초기화 완료. {_byId.Count}개 챕터");
    }

    // ── 조회 ──

    /// <summary>chapter_id로 서버 엔트리 조회.</summary>
    public ChapterServerEntry Get(string chapterId)
    {
        return _byId.TryGetValue(chapterId, out var entry) ? entry : null;
    }

    /// <summary>ChapterId enum으로 조회.</summary>
    public ChapterServerEntry Get(ChapterId chapter)
    {
        return Get(chapter.ToString());
    }

    /// <summary>전체 엔트리 목록.</summary>
    public IReadOnlyDictionary<string, ChapterServerEntry> All => _byId;

    // ── 내부 ──

    private void AddEntry(ChapterServerEntry entry)
    {
        if (entry == null || string.IsNullOrEmpty(entry.chapter_id)) return;

        // stat_version 비교: 기존보다 높을 때만 갱신
        if (_byId.TryGetValue(entry.chapter_id, out var existing))
        {
            if (entry.stat_version <= existing.stat_version)
                return;
        }

        _byId[entry.chapter_id] = entry;
    }

    private void LoadFromJson()
    {
        try
        {
            var json = File.ReadAllText(FilePath);
            var col = JsonUtility.FromJson<ChapterEntryCollection>(json);
            if (col?.chapters == null) return;
            _byId.Clear();
            foreach (var entry in col.chapters)
                AddEntry(entry);
        }
        catch (Exception e) { Debug.LogError($"[ChapterDataManager] 로컬 로드 실패: {e.Message}"); }
    }

    private void InitializeFromJson(string jsonText)
    {
        var col = JsonUtility.FromJson<ChapterEntryCollection>(jsonText);
        if (col?.chapters == null) return;
        foreach (var entry in col.chapters)
            AddEntry(entry);
    }

    private void SaveToJson()
    {
        var all = new List<ChapterServerEntry>(_byId.Values);
        var col = new ChapterEntryCollection { chapters = all };
        File.WriteAllText(FilePath, JsonUtility.ToJson(col, true));
    }

    private async UniTask LoadFromServerAsync()
    {
        int loaded = ChartLoader.Load("CHAPTER_DATA", row =>
        {
            var entry = ParseRow(row);
            if (entry != null) AddEntry(entry);
        });

        if (loaded > 0) SaveToJson();

        await UniTask.CompletedTask;
    }

    private static ChapterServerEntry ParseRow(JsonData row)
    {
        try
        {
            return new ChapterServerEntry
            {
                chapter_id          = row.TryGetString("chapter_id"),
                chapter_name        = row.TryGetString("chapter_name"),
                description         = row.TryGetString("description"),
                map_bg_key          = row.TryGetString("map_bg_key"),
                bgm_key             = row.TryGetString("bgm_key"),
                difficulty_scale    = row.TryGetFloat("difficulty_scale"),
                monster_count_scale = row.TryGetFloat("monster_count_scale"),
                monster_pool_tag    = row.TryGetString("monster_pool_tag"),
                gold_multiplier     = row.TryGetFloat("gold_multiplier"),
                item_drop_multiplier = row.TryGetFloat("item_drop_multiplier"),
                zone_layout_key     = row.TryGetString("zone_layout_key"),
                total_layers        = row.TryGetInt("total_layers"),
                peak_layer          = row.TryGetInt("peak_layer"),
                stat_version        = row.TryGetInt("stat_version"),
            };
        }
        catch { return null; }
    }
}

[System.Serializable]
public class ChapterEntryCollection
{
    public List<ChapterServerEntry> chapters;
}
