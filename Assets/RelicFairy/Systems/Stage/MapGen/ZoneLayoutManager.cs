using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Cysharp.Threading.Tasks;
using LitJson;
using UnityEngine;

/// <summary>방 연결 방향 비트마스크. North=+Z, South=-Z, East=+X, West=-X.</summary>
[Flags]
public enum DoorMask
{
    None  = 0,
    North = 1 << 0,
    East  = 1 << 1,
    South = 1 << 2,
    West  = 1 << 3,
}

/// <summary>
/// 뒤끝 CDN에서 챕터별 존 레이아웃(CHAPTER_1_ZONE_LAYOUT 등)을 로드/캐시.
/// zone_layout_key로 로드 요청 → 내부적으로 ToUpper() 후 ChartLoader 호출.
/// </summary>
public class ZoneLayoutManager
{
    private const string CacheFilePrefix = "zone_layout_";
    private const int    DoorWidth       = 5;

    private readonly Dictionary<string, List<ZoneLayoutEntry>> _cache = new();
    private readonly Dictionary<string, List<ZonePoolEntry>>   _poolCache = new();
    private readonly Dictionary<string, List<ZoneMapSlot>>     _slotCache = new();
    private readonly System.Random _rng = new();

    // ── 조회 ────────────────────────────────────

    public bool IsLoaded(string layoutKey) => _cache.ContainsKey(layoutKey);

    public List<ZoneLayoutEntry> GetZones(string layoutKey)
    {
        _cache.TryGetValue(layoutKey, out var list);
        return list;
    }

    // ── 2-CSV 로드 (zone_pool + chapter_map → merge) ──────────────────────

    /// <summary>
    /// mapKey(chapter_map) + poolKey(zone_pool) 두 CSV를 Addressables에서 로드하여 병합한다.
    /// 결과는 layoutKey = mapKey 로 _cache에 저장되므로 GetZones(mapKey)로 조회 가능.
    /// </summary>
    public async UniTask LoadWithPoolAsync(string mapKey, string poolKey, string cacheAs = null)
    {
        var key = string.IsNullOrEmpty(cacheAs) ? mapKey : cacheAs;
        if (_cache.ContainsKey(key)) return;

        // ── 슬롯 CSV 로드 ──
        if (!_slotCache.ContainsKey(mapKey))
        {
            var slots = new List<ZoneMapSlot>();
            var slotAsset = await Managers.AddressableManager.TryLoadAssetAsync<TextAsset>(mapKey.ToUpper());
            if (slotAsset != null)
                ParseSlotCsv(slotAsset.text.TrimStart(), slots);
            _slotCache[mapKey] = slots;
        }

        // ── 풀 CSV 로드 ──
        if (!_poolCache.ContainsKey(poolKey))
        {
            var pool = new List<ZonePoolEntry>();
            var poolAsset = await Managers.AddressableManager.TryLoadAssetAsync<TextAsset>(poolKey.ToUpper());
            if (poolAsset != null)
                ParsePoolCsv(poolAsset.text.TrimStart(), pool);
            _poolCache[poolKey] = pool;
        }

        _cache[key] = MergeSlotPool(_slotCache[mapKey], _poolCache[poolKey]);
        Debug.Log($"[ZoneLayoutManager] LoadWithPoolAsync '{mapKey}' → {_cache[key].Count}개 존 (cacheAs:{key})");
    }

    /// <summary>
    /// 룸 풀 CSV(zone_pool)를 로드/파싱해 ZonePoolEntry 리스트를 반환한다.
    /// 절차적 생성(RunSequencer)에서 슬롯 병합 없이 풀만 필요할 때 사용. 결과는 _poolCache에 캐시.
    /// </summary>
    public async UniTask<List<ZonePoolEntry>> LoadPoolAsync(string poolKey)
    {
        if (string.IsNullOrEmpty(poolKey)) return new List<ZonePoolEntry>();
        if (_poolCache.TryGetValue(poolKey, out var cached) && cached.Count > 0) return cached;

        var pool      = new List<ZonePoolEntry>();
        var chartName = poolKey.ToUpper(); // 뒤끝 차트명은 대문자

        // 1) 서버(뒤끝 CDN) 차트 우선 — 존 레이아웃과 동일 경로
        try
        {
            int loaded = ChartLoader.Load(chartName, row =>
            {
                var entry = ParsePoolRow(row);
                if (entry != null && !string.IsNullOrEmpty(entry.pool_key))
                    pool.Add(entry);
            });
            if (loaded > 0)
                Debug.Log($"[ZoneLayoutManager] LoadPoolAsync 서버 '{chartName}' → {pool.Count}개");
        }
        catch (System.Exception ex)
        {
            Debug.LogWarning($"[ZoneLayoutManager] LoadPoolAsync 서버 예외 ({chartName}): {ex.Message}");
        }

        // 2) Addressables 폴백 (오프라인/에디터 테스트)
        if (pool.Count == 0)
        {
            var asset = await Managers.AddressableManager.TryLoadAssetAsync<TextAsset>(chartName);
            if (asset != null)
            {
                ParsePoolCsv(asset.text.TrimStart(), pool);
                Debug.Log($"[ZoneLayoutManager] LoadPoolAsync Addressables 폴백 '{chartName}' → {pool.Count}개");
            }
            else
            {
                Debug.LogWarning($"[ZoneLayoutManager] LoadPoolAsync: '{chartName}' 서버·Addressables 모두 없음");
            }
        }

        _poolCache[poolKey] = pool;
        return pool;
    }

    /// <summary>뒤끝 CDN 차트 행(JsonData) → ZonePoolEntry. ParsePoolCsv(CSV)의 서버판.</summary>
    private static ZonePoolEntry ParsePoolRow(JsonData row)
    {
        try
        {
            return new ZonePoolEntry
            {
                pool_key            = row.TryGetString("pool_key"),
                category            = row.TryGetString("category"),
                size_tag            = row.TryGetString("size_tag"),
                grid_width          = row.TryGetInt("grid_width"),
                grid_height         = row.TryGetInt("grid_height"),
                grid_csv            = row.TryGetString("grid_csv"),
                spawn_local_x       = row.TryGetFloat("spawn_local_x"),
                spawn_local_z       = row.TryGetFloat("spawn_local_z"),
                theme               = row.TryGetString("theme"),
                palette             = row.TryGetString("palette"),
                arena_template_key  = row.TryGetString("arena_template_key"),
                difficulty_scale    = row.TryGetFloat("difficulty_scale"),
                scatter_range       = row.TryGetFloat("scatter_range"),
                clear_overlay_delay = row.TryGetFloat("clear_overlay_delay"),
                max_active_spawners = row.TryGetInt("max_active_spawners"),
                stat_version        = row.TryGetInt("stat_version"),
            };
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"[ZoneLayoutManager] 풀 행 파싱 실패: {e.Message}");
            return null;
        }
    }

    private List<ZoneLayoutEntry> MergeSlotPool(List<ZoneMapSlot> slots, List<ZonePoolEntry> pool)
    {
        var result    = new List<ZoneLayoutEntry>();
        var slotById  = new Dictionary<int, ZoneMapSlot>();
        foreach (var s in slots) slotById[s.slot_id] = s;

        var doorMasks  = CalcDoorMasks(slots, slotById);
        var cooldowns  = new Dictionary<string, int>(); // pool_key → 남은 쿨다운 턴

        foreach (var slot in slots)
        {
            var category = string.IsNullOrEmpty(slot.fixed_category) ? "Normal" : slot.fixed_category;

            var candidates = pool.FindAll(p =>
                string.Equals(p.category, category, StringComparison.OrdinalIgnoreCase) &&
                (string.IsNullOrEmpty(slot.theme) ||
                 string.Equals(p.theme, slot.theme, StringComparison.OrdinalIgnoreCase)));

            if (candidates.Count == 0)
                candidates = pool.FindAll(p => string.Equals(p.category, category, StringComparison.OrdinalIgnoreCase));

            if (candidates.Count == 0)
            {
                Debug.LogWarning($"[ZoneLayoutManager] 슬롯 {slot.slot_id} 에 맞는 풀 없음 (category={category})");
                continue;
            }

            var pick = PickWithCooldown(candidates, cooldowns);
            var mask = doorMasks.TryGetValue(slot.slot_id, out var m) ? m : DoorMask.None;

            result.Add(new ZoneLayoutEntry
            {
                zone_index          = slot.slot_id,
                layer_index         = slot.layer,
                category            = category,
                label               = slot.label,
                world_center_x      = slot.lane * 55f,
                world_center_y      = 0f,
                world_center_z      = slot.layer * 55f,
                grid_width          = pick.grid_width,
                grid_height         = pick.grid_height,
                next_zone_indices   = slot.next_slots,
                sink_depth          = 0f,
                platform_prefab_key = "",
                spawn_local_x       = pick.spawn_local_x,
                spawn_local_z       = pick.spawn_local_z,
                difficulty_scale    = pick.difficulty_scale,
                theme               = string.IsNullOrEmpty(slot.theme) ? pick.theme : slot.theme,
                palette             = pick.palette,
                grid_csv            = PunchDoors(pick.grid_csv, pick.grid_width, pick.grid_height, mask),
                arena_template_key  = pick.arena_template_key,
                scatter_range       = pick.scatter_range,
                clear_overlay_delay = pick.clear_overlay_delay,
                max_active_spawners = pick.max_active_spawners,
                corridor_style      = slot.corridor_style,
                stat_version        = pick.stat_version,
            });
        }

        result.Sort((a, b) => a.zone_index.CompareTo(b.zone_index));
        return result;
    }

    /// <summary>슬롯 연결(next_slots + lane/layer 차이)으로 각 슬롯의 필요 문 방향을 계산한다.</summary>
    private static Dictionary<int, DoorMask> CalcDoorMasks(List<ZoneMapSlot> slots, Dictionary<int, ZoneMapSlot> slotById)
    {
        var masks = new Dictionary<int, DoorMask>();
        foreach (var s in slots) masks[s.slot_id] = DoorMask.None;

        foreach (var slot in slots)
        {
            if (string.IsNullOrEmpty(slot.next_slots)) continue;
            foreach (var part in slot.next_slots.Split('|'))
            {
                if (!int.TryParse(part.Trim(), out int nextId)) continue;
                if (!slotById.TryGetValue(nextId, out var next)) continue;

                int dLane  = next.lane  - slot.lane;
                int dLayer = next.layer - slot.layer;

                if (dLayer > 0) { masks[slot.slot_id] |= DoorMask.North; masks[next.slot_id] |= DoorMask.South; }
                else if (dLayer < 0) { masks[slot.slot_id] |= DoorMask.South; masks[next.slot_id] |= DoorMask.North; }

                if (dLane > 0) { masks[slot.slot_id] |= DoorMask.East; masks[next.slot_id] |= DoorMask.West; }
                else if (dLane < 0) { masks[slot.slot_id] |= DoorMask.West; masks[next.slot_id] |= DoorMask.East; }
            }
        }
        return masks;
    }

    /// <summary>풀 룸 grid_csv의 해당 방향 벽 중앙을 Floor로 뚫어 문을 만든다.</summary>
    private static string PunchDoors(string gridCsv, int width, int height, DoorMask mask)
    {
        if (string.IsNullOrEmpty(gridCsv) || mask == DoorMask.None) return gridCsv;

        var grid = MapDataLoader.Parse(gridCsv);
        if (grid == null) return gridCsv;

        int w    = grid.GetLength(0);
        int h    = grid.GetLength(1);
        int half = DoorWidth / 2;

        if ((mask & DoorMask.North) != 0)
        {
            int cx = w / 2;
            for (int x = cx - half; x <= cx + half; x++)
                if (x >= 0 && x < w) grid[x, h - 1] = TileType.Floor;
        }
        if ((mask & DoorMask.South) != 0)
        {
            int cx = w / 2;
            for (int x = cx - half; x <= cx + half; x++)
                if (x >= 0 && x < w) grid[x, 0] = TileType.Floor;
        }
        if ((mask & DoorMask.East) != 0)
        {
            int cz = h / 2;
            for (int z = cz - half; z <= cz + half; z++)
                if (z >= 0 && z < h) grid[w - 1, z] = TileType.Floor;
        }
        if ((mask & DoorMask.West) != 0)
        {
            int cz = h / 2;
            for (int z = cz - half; z <= cz + half; z++)
                if (z >= 0 && z < h) grid[0, z] = TileType.Floor;
        }

        return MapDataLoader.Serialize(grid);
    }

    /// <summary>쿨다운을 고려해 후보 중 하나를 선택한다. 쿨다운 = max(3, 풀 크기 × 40%).</summary>
    private ZonePoolEntry PickWithCooldown(List<ZonePoolEntry> candidates, Dictionary<string, int> cooldowns)
    {
        var available = candidates.FindAll(p => !cooldowns.TryGetValue(p.pool_key, out int cd) || cd <= 0);
        if (available.Count == 0) available = candidates; // 폴백: 쿨다운 무시

        var pick     = available[_rng.Next(available.Count)];
        int cooldown = Math.Max(3, (int)(candidates.Count * 0.4f));
        cooldowns[pick.pool_key] = cooldown;

        // 기존 쿨다운 감소
        var keys = new List<string>(cooldowns.Keys);
        foreach (var key in keys)
            if (key != pick.pool_key) cooldowns[key]--;

        return pick;
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
            // 뒤끝 차트 이름은 대문자 사용 (예: "CHAPTER_1_ZONE_LAYOUT")
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
                has_hidden_reward        = row.TryGetString("has_hidden_reward").Equals("true", StringComparison.OrdinalIgnoreCase),
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
                corridor_style           = row.TryGetString("corridor_style"),
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
                    has_hidden_reward        = CsvStr(cols, colMap, "has_hidden_reward").Equals("true", StringComparison.OrdinalIgnoreCase),
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
                    corridor_style           = CsvStr(cols, colMap, "corridor_style"),
                });
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[ZoneLayoutManager] CSV 행 {r} 파싱 실패: {e.Message}");
            }
        }
    }

    // ── 2-CSV 파서 ─────────────────────────────────────────────────────────

    private static void ParseSlotCsv(string csvText, List<ZoneMapSlot> slots)
    {
        var lines = csvText.Replace("\r\n", "\n").Split('\n');
        if (lines.Length < 2) return;
        var headers = SplitCsvRow(lines[0]);
        var colMap  = new Dictionary<string, int>(StringComparer.Ordinal);
        for (int i = 0; i < headers.Count; i++) colMap[headers[i].Trim()] = i;

        for (int r = 1; r < lines.Length; r++)
        {
            var line = lines[r].Trim();
            if (string.IsNullOrEmpty(line)) continue;
            var cols = SplitCsvRow(line);
            try
            {
                slots.Add(new ZoneMapSlot
                {
                    slot_id        = CsvInt(cols, colMap, "slot_id"),
                    lane           = CsvInt(cols, colMap, "lane"),
                    layer          = CsvInt(cols, colMap, "layer"),
                    fixed_category = CsvStr(cols, colMap, "fixed_category"),
                    next_slots     = CsvStr(cols, colMap, "next_slots"),
                    label          = CsvStr(cols, colMap, "label"),
                    theme          = CsvStr(cols, colMap, "theme"),
                    corridor_style = CsvStr(cols, colMap, "corridor_style"),
                });
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[ZoneLayoutManager] SlotCSV 행 {r} 파싱 실패: {e.Message}");
            }
        }
    }

    private static void ParsePoolCsv(string csvText, List<ZonePoolEntry> pool)
    {
        var lines = csvText.Replace("\r\n", "\n").Split('\n');
        if (lines.Length < 2) return;
        var headers = SplitCsvRow(lines[0]);
        var colMap  = new Dictionary<string, int>(StringComparer.Ordinal);
        for (int i = 0; i < headers.Count; i++) colMap[headers[i].Trim()] = i;

        for (int r = 1; r < lines.Length; r++)
        {
            var line = lines[r].Trim();
            if (string.IsNullOrEmpty(line)) continue;
            var cols = SplitCsvRow(line);
            try
            {
                pool.Add(new ZonePoolEntry
                {
                    pool_key            = CsvStr(cols, colMap, "pool_key"),
                    category            = CsvStr(cols, colMap, "category"),
                    size_tag            = CsvStr(cols, colMap, "size_tag"),
                    grid_width          = CsvInt(cols, colMap, "grid_width"),
                    grid_height         = CsvInt(cols, colMap, "grid_height"),
                    grid_csv            = CsvStr(cols, colMap, "grid_csv"),
                    spawn_local_x       = CsvFloat(cols, colMap, "spawn_local_x"),
                    spawn_local_z       = CsvFloat(cols, colMap, "spawn_local_z"),
                    theme               = CsvStr(cols, colMap, "theme"),
                    palette             = CsvStr(cols, colMap, "palette"),
                    arena_template_key  = CsvStr(cols, colMap, "arena_template_key"),
                    difficulty_scale    = CsvFloat(cols, colMap, "difficulty_scale"),
                    scatter_range       = CsvFloat(cols, colMap, "scatter_range"),
                    clear_overlay_delay = CsvFloat(cols, colMap, "clear_overlay_delay"),
                    max_active_spawners = CsvInt(cols, colMap, "max_active_spawners"),
                    stat_version        = CsvInt(cols, colMap, "stat_version"),
                });
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[ZoneLayoutManager] PoolCSV 행 {r} 파싱 실패: {e.Message}");
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
