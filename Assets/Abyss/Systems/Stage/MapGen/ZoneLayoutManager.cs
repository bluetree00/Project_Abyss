using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Cysharp.Threading.Tasks;
using LitJson;
using UnityEngine;

/// <summary>
/// 뒤끝 CDN에서 챕터별 존 레이아웃(CHAPTER1_ZONE_LAYOUT 등)을 로드/캐시.
/// zone_layout_key로 로드 요청 → 내부적으로 ToUpper() 후 ChartLoader 호출.
/// </summary>
public class ZoneLayoutManager
{
    private const string CacheFilePrefix = "zone_layout_";

    private readonly Dictionary<string, List<ZoneLayoutEntry>> _cache = new();

    // ── 조회 ────────────────────────────────────

    public bool IsLoaded(string layoutKey) => _cache.ContainsKey(layoutKey);

    public List<ZoneLayoutEntry> GetZones(string layoutKey)
    {
        _cache.TryGetValue(layoutKey, out var list);
        return list;
    }

    // ── 초기화 ──────────────────────────────────

    public async UniTask LoadAsync(string layoutKey)
    {
        if (string.IsNullOrEmpty(layoutKey) || _cache.ContainsKey(layoutKey)) return;

        var entries = new List<ZoneLayoutEntry>();
        var filePath = GetFilePath(layoutKey);

        if (File.Exists(filePath))
            LoadFromJson(entries, filePath, layoutKey);

        await LoadFromServerAsync(layoutKey, entries);

        if (entries.Count == 0)
        {
            Debug.Log($"[ZoneLayoutManager] CDN/캐시 실패 — Addressables 폴백 ({layoutKey.ToUpper()})");
            var textAsset = await Managers.AddressableManager.TryLoadAssetAsync<TextAsset>(layoutKey.ToUpper());
            if (textAsset != null)
            {
                var text = textAsset.text.TrimStart();
                if (text.StartsWith("{"))
                {
                    var col = JsonUtility.FromJson<ZoneLayoutCollection>(text);
                    if (col?.zones != null) entries.AddRange(col.zones);
                }
                else
                {
                    ParseCsvFallback(text, entries);
                }
            }
            else
            {
                Debug.LogWarning($"[ZoneLayoutManager] Addressables에도 '{layoutKey.ToUpper()}' 없음");
            }
        }

        _cache[layoutKey] = entries;
        Debug.Log($"[ZoneLayoutManager] '{layoutKey}' 로드 완료: {entries.Count}개 존");
    }

    // ── 내부: 로컬 캐시 ─────────────────────────

    private static string GetFilePath(string layoutKey)
        => Path.Combine(Application.persistentDataPath, $"{CacheFilePrefix}{layoutKey}.json");

    private static void LoadFromJson(List<ZoneLayoutEntry> entries, string filePath, string layoutKey)
    {
        try
        {
            var json = File.ReadAllText(filePath);
            var col = JsonUtility.FromJson<ZoneLayoutCollection>(json);
            if (col?.zones == null) return;
            entries.AddRange(col.zones);
            Debug.Log($"[ZoneLayoutManager] 로컬 로드 ({layoutKey}): {entries.Count}개");
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[ZoneLayoutManager] 로컬 로드 실패 ({layoutKey}): {e.Message}");
        }
    }

    private static void SaveToJson(string layoutKey, List<ZoneLayoutEntry> entries)
    {
        try
        {
            var col = new ZoneLayoutCollection
            {
                layout_key = layoutKey,
                zones = new List<ZoneLayoutEntry>(entries),
            };
            File.WriteAllText(GetFilePath(layoutKey), JsonUtility.ToJson(col, true));
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[ZoneLayoutManager] 저장 실패 ({layoutKey}): {e.Message}");
        }
    }

    // ── 내부: 뒤끝 CDN ──────────────────────────

    private static async UniTask LoadFromServerAsync(string layoutKey, List<ZoneLayoutEntry> entries)
    {
        try
        {
            // 뒤끝 차트 이름은 대문자 사용 (예: "CHAPTER1_ZONE_LAYOUT")
            var chartName = layoutKey.ToUpper();
            var newEntries = new List<ZoneLayoutEntry>();

            int loaded = ChartLoader.Load(chartName, row =>
            {
                var entry = ParseRow(row);
                if (entry == null) return;

                // stat_version 기반 중복 갱신
                var existing = entries.Find(e => e.zone_index == entry.zone_index);
                if (existing != null)
                {
                    if (entry.stat_version <= existing.stat_version) return;
                    entries.Remove(existing);
                }
                newEntries.Add(entry);
            });

            Debug.Log($"[ZoneLayoutManager] ChartLoader.Load('{chartName}') → {loaded}개 raw, {newEntries.Count}개 파싱됨");

            if (newEntries.Count > 0)
            {
                entries.AddRange(newEntries);
                entries.Sort((a, b) => a.zone_index.CompareTo(b.zone_index));
                SaveToJson(layoutKey, entries);
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[ZoneLayoutManager] CDN 로드 실패 ({layoutKey}): {e.Message}");
        }

        await UniTask.CompletedTask;
    }

    private static ZoneLayoutEntry ParseRow(JsonData row)
    {
        try
        {
            return new ZoneLayoutEntry
            {
                zone_index               = row.TryGetInt("zone_index"),
                layer_index              = row.TryGetInt("layer_index"),
                category                 = row.TryGetString("category"),
                label                    = row.TryGetString("label"),
                world_center_x           = row.TryGetFloat("world_center_x"),
                world_center_y           = row.TryGetFloat("world_center_y"),
                world_center_z           = row.TryGetFloat("world_center_z"),
                grid_width               = row.TryGetInt("grid_width"),
                grid_height              = row.TryGetInt("grid_height"),
                next_zone_indices        = row.TryGetString("next_zone_indices"),
                sink_depth               = row.TryGetFloat("sink_depth"),
                platform_prefab_key      = row.TryGetString("platform_prefab_key"),
                spawn_local_x            = row.TryGetFloat("spawn_local_x"),
                spawn_local_z            = row.TryGetFloat("spawn_local_z"),
                difficulty_scale         = row.TryGetFloat("difficulty_scale"),
                has_hidden_reward        = row.TryGetString("has_hidden_reward") == "true",
                hidden_reward_local_x    = row.TryGetFloat("hidden_reward_local_x"),
                hidden_reward_local_y    = row.TryGetFloat("hidden_reward_local_y"),
                hidden_reward_local_z    = row.TryGetFloat("hidden_reward_local_z"),
                hidden_reward_prefab_key = row.TryGetString("hidden_reward_prefab_key"),
                arena_template_key       = row.TryGetString("arena_template_key"),
                theme                    = row.TryGetString("theme"),
                palette                  = row.TryGetString("palette"),
                grid_csv                 = row.TryGetString("grid_csv"),
                layout_rule              = row.TryGetString("layout_rule"),
                transition_type          = row.TryGetString("transition_type"),
                scatter_range            = row.TryGetFloat("scatter_range"),
                clear_overlay_delay      = row.TryGetFloat("clear_overlay_delay"),
                max_active_spawners      = row.TryGetInt("max_active_spawners"),
                stat_version             = row.TryGetInt("stat_version"),
            };
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[ZoneLayoutManager] 행 파싱 실패: {e.Message}");
            return null;
        }
    }

    // ── CSV 폴백 파서 ────────────────────────────────────────

    private static void ParseCsvFallback(string csvText, List<ZoneLayoutEntry> entries)
    {
        var lines = csvText.Replace("\r\n", "\n").Split('\n');
        if (lines.Length < 2) return;

        var headers = SplitCsvRow(lines[0]);
        var colMap = new Dictionary<string, int>(StringComparer.Ordinal);
        for (int i = 0; i < headers.Count; i++)
            colMap[headers[i].Trim()] = i;

        for (int r = 1; r < lines.Length; r++)
        {
            var line = lines[r].Trim();
            if (string.IsNullOrEmpty(line)) continue;

            var cols = SplitCsvRow(line);
            try
            {
                entries.Add(new ZoneLayoutEntry
                {
                    zone_index               = CsvInt(cols, colMap, "zone_index"),
                    layer_index              = CsvInt(cols, colMap, "layer_index"),
                    category                 = CsvStr(cols, colMap, "category"),
                    label                    = CsvStr(cols, colMap, "label"),
                    world_center_x           = CsvFloat(cols, colMap, "world_center_x"),
                    world_center_y           = CsvFloat(cols, colMap, "world_center_y"),
                    world_center_z           = CsvFloat(cols, colMap, "world_center_z"),
                    grid_width               = CsvInt(cols, colMap, "grid_width"),
                    grid_height              = CsvInt(cols, colMap, "grid_height"),
                    next_zone_indices        = CsvStr(cols, colMap, "next_zone_indices"),
                    sink_depth               = CsvFloat(cols, colMap, "sink_depth"),
                    platform_prefab_key      = CsvStr(cols, colMap, "platform_prefab_key"),
                    spawn_local_x            = CsvFloat(cols, colMap, "spawn_local_x"),
                    spawn_local_z            = CsvFloat(cols, colMap, "spawn_local_z"),
                    difficulty_scale         = CsvFloat(cols, colMap, "difficulty_scale"),
                    has_hidden_reward        = CsvStr(cols, colMap, "has_hidden_reward") == "true",
                    hidden_reward_local_x    = CsvFloat(cols, colMap, "hidden_reward_local_x"),
                    hidden_reward_local_y    = CsvFloat(cols, colMap, "hidden_reward_local_y"),
                    hidden_reward_local_z    = CsvFloat(cols, colMap, "hidden_reward_local_z"),
                    hidden_reward_prefab_key = CsvStr(cols, colMap, "hidden_reward_prefab_key"),
                    arena_template_key       = CsvStr(cols, colMap, "arena_template_key"),
                    theme                    = CsvStr(cols, colMap, "theme"),
                    palette                  = CsvStr(cols, colMap, "palette"),
                    grid_csv                 = CsvStr(cols, colMap, "grid_csv"),
                    layout_rule              = CsvStr(cols, colMap, "layout_rule"),
                    transition_type          = CsvStr(cols, colMap, "transition_type"),
                    scatter_range            = CsvFloat(cols, colMap, "scatter_range"),
                    clear_overlay_delay      = CsvFloat(cols, colMap, "clear_overlay_delay"),
                    max_active_spawners      = CsvInt(cols, colMap, "max_active_spawners"),
                    stat_version             = CsvInt(cols, colMap, "stat_version"),
                });
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[ZoneLayoutManager] CSV 행 {r} 파싱 실패: {e.Message}");
            }
        }
    }

    private static string CsvStr(List<string> cols, Dictionary<string, int> map, string key)
        => map.TryGetValue(key, out int i) && i < cols.Count ? cols[i] : "";

    private static int CsvInt(List<string> cols, Dictionary<string, int> map, string key)
        => int.TryParse(CsvStr(cols, map, key), out int v) ? v : 0;

    private static float CsvFloat(List<string> cols, Dictionary<string, int> map, string key)
        => float.TryParse(CsvStr(cols, map, key), NumberStyles.Float, CultureInfo.InvariantCulture, out float v) ? v : 0f;

    private static List<string> SplitCsvRow(string row)
    {
        var fields = new List<string>();
        var sb = new StringBuilder();
        bool inQuotes = false;

        for (int i = 0; i < row.Length; i++)
        {
            char c = row[i];
            if (c == '"')
            {
                inQuotes = !inQuotes;
            }
            else if (c == ',' && !inQuotes)
            {
                fields.Add(sb.ToString());
                sb.Clear();
            }
            else
            {
                sb.Append(c);
            }
        }
        fields.Add(sb.ToString());
        return fields;
    }
}
