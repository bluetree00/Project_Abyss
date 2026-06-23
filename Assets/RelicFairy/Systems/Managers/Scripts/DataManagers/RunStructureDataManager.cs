using UnityEngine;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Cysharp.Threading.Tasks;
using LitJson;

/// <summary>
/// 뒤끝 CDN에서 RUN_STRUCTURE 로드(런 구조 정본). chapter_id로 조회.
/// 서버에 엔트리가 없으면 RunFlowController가 SO(RunStructureConfig)로 폴백한다.
/// 곡선/마일스톤은 한 셀에 '|' 구분 팩 문자열로 인코딩 → RunStructureEntry가 파싱.
/// </summary>
public class RunStructureDataManager
{
    private const string DataFileName = "run_structure.json";
    private string FilePath => Path.Combine(Application.persistentDataPath, DataFileName);

    private readonly Dictionary<string, RunStructureEntry> _byId = new();

    public bool IsInitialized { get; private set; }

    // ── 초기화 ──

    public async UniTask InitializeAsync()
    {
        if (File.Exists(FilePath)) LoadFromJson();

        try { await LoadFromServerAsync(); }
        catch (Exception e) { Debug.LogWarning($"[RunStructureDataManager] CDN 예외: {e.Message}"); }

        IsInitialized = true;
        Debug.Log($"[RunStructureDataManager] 초기화 완료. {_byId.Count}개 챕터");
    }

    // ── 조회 ──

    /// <summary>chapter_id로 런 구조 조회(없으면 null → 호출자가 SO 폴백).</summary>
    public IRunStructure Get(string chapterId)
        => _byId.TryGetValue(chapterId, out var entry) ? entry : null;

    /// <summary>ChapterId enum으로 조회.</summary>
    public IRunStructure Get(ChapterId chapter) => Get(chapter.ToString());

    // ── 내부 ──

    private void AddEntry(RunStructureEntry entry)
    {
        if (entry == null || string.IsNullOrEmpty(entry.chapter_id)) return;

        // stat_version 비교: 기존보다 높을 때만 갱신
        if (_byId.TryGetValue(entry.chapter_id, out var existing) && entry.stat_version <= existing.stat_version)
            return;

        _byId[entry.chapter_id] = entry;
    }

    private void LoadFromJson()
    {
        try
        {
            var json = File.ReadAllText(FilePath);
            var col = JsonUtility.FromJson<RunStructureCollection>(json);
            if (col?.entries == null) return;
            _byId.Clear();
            foreach (var entry in col.entries) AddEntry(entry);
        }
        catch (Exception e) { Debug.LogError($"[RunStructureDataManager] 로컬 로드 실패: {e.Message}"); }
    }

    private void SaveToJson()
    {
        var col = new RunStructureCollection { entries = new List<RunStructureEntry>(_byId.Values) };
        File.WriteAllText(FilePath, JsonUtility.ToJson(col, true));
    }

    private async UniTask LoadFromServerAsync()
    {
        int loaded = ChartLoader.Load("RUN_STRUCTURE", row =>
        {
            var entry = ParseRow(row);
            if (entry != null) AddEntry(entry);
        });

        if (loaded > 0) SaveToJson();

        await UniTask.CompletedTask;
    }

    private static RunStructureEntry ParseRow(JsonData row)
    {
        try
        {
            return new RunStructureEntry
            {
                chapter_id       = row.TryGetString("chapter_id"),
                boss_threshold   = row.TryGetInt("boss_threshold"),
                preboss_pool_key = row.TryGetString("preboss_pool_key"),
                boss_pool_key    = row.TryGetString("boss_pool_key"),
                shop_chance      = row.TryGetFloat("shop_chance"),
                shop_cap         = row.TryGetInt("shop_cap"),
                event_chance     = row.TryGetFloat("event_chance"),
                event_cap        = row.TryGetInt("event_cap"),
                elite_chance     = row.TryGetFloat("elite_chance"),
                difficulty_curve = row.TryGetString("difficulty_curve"),
                milestones       = row.TryGetString("milestones"),
                stat_version     = row.TryGetInt("stat_version"),
            };
        }
        catch { return null; }
    }
}

/// <summary>
/// CSV 한 행 = 한 챕터의 런 구조. IRunStructure를 직접 구현해 RunSequencer에 그대로 주입된다.
/// difficulty_curve/milestones는 팩 문자열("visit:값|visit:값")이며 첫 사용 시 1회 파싱한다.
/// </summary>
[Serializable]
public class RunStructureEntry : IRunStructure
{
    public string chapter_id;
    public int    boss_threshold = 12;
    public string preboss_pool_key;
    public string boss_pool_key;
    public float  shop_chance = 0.25f;
    public int    shop_cap = 2;
    public float  event_chance = 0.2f;
    public int    event_cap = 2;
    public float  elite_chance = 0.15f;
    public string difficulty_curve;   // 예: "0:0.4|6:1.0|12:1.8" (구간선형)
    public string milestones;         // 예: "4:Shop|7:Event|10:Elite"
    public int    stat_version;

    [NonSerialized] private List<Vector2> _curve;
    [NonSerialized] private Dictionary<int, RoomPlanKind> _milestoneMap;
    [NonSerialized] private bool _built;

    public int    BossThreshold      => boss_threshold;
    public string PreBossRoomKey     => preboss_pool_key;
    public string BossRoomKey        => boss_pool_key;
    public int    ShopMaxPerChapter  => shop_cap;
    public float  ShopChance         => shop_chance;
    public int    EventMaxPerChapter => event_cap;
    public float  EventChance        => event_chance;
    public float  EliteChance        => elite_chance;

    public float DifficultyAt(int visitCount)
    {
        Build();
        if (_curve == null || _curve.Count == 0) return 0f;
        if (visitCount <= _curve[0].x) return _curve[0].y;
        var last = _curve[_curve.Count - 1];
        if (visitCount >= last.x) return last.y;
        for (int i = 1; i < _curve.Count; i++)
        {
            if (visitCount <= _curve[i].x)
            {
                var a = _curve[i - 1];
                var b = _curve[i];
                return Mathf.Lerp(a.y, b.y, Mathf.InverseLerp(a.x, b.x, visitCount));
            }
        }
        return last.y;
    }

    public RoomPlanKind? GetMilestoneKind(int visitIndex)
    {
        Build();
        if (_milestoneMap != null && _milestoneMap.TryGetValue(visitIndex, out var kind)) return kind;
        return null;
    }

    private void Build()
    {
        if (_built) return;
        _built = true;
        _curve = ParseCurve(difficulty_curve);
        _milestoneMap = ParseMilestones(milestones);
    }

    private static List<Vector2> ParseCurve(string packed)
    {
        var list = new List<Vector2>();
        if (string.IsNullOrWhiteSpace(packed)) return list;
        foreach (var pair in packed.Split('|'))
        {
            var kv = pair.Split(':');
            if (kv.Length != 2) continue;
            if (int.TryParse(kv[0].Trim(), out int visit) &&
                float.TryParse(kv[1].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float val))
                list.Add(new Vector2(visit, val));
        }
        list.Sort((a, b) => a.x.CompareTo(b.x));
        return list;
    }

    private static Dictionary<int, RoomPlanKind> ParseMilestones(string packed)
    {
        var map = new Dictionary<int, RoomPlanKind>();
        if (string.IsNullOrWhiteSpace(packed)) return map;
        foreach (var token in packed.Split('|'))
        {
            var kv = token.Split(':');
            if (kv.Length < 2) continue;
            if (int.TryParse(kv[0].Trim(), out int visit) &&
                Enum.TryParse(kv[1].Trim(), true, out RoomPlanKind kind))
                map[visit] = kind;
        }
        return map;
    }
}

[Serializable]
public class RunStructureCollection
{
    public List<RunStructureEntry> entries;
}
