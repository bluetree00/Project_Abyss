using System;
using System.Collections.Generic;
using System.IO;
using Cysharp.Threading.Tasks;
using LitJson;
using UnityEngine;

/// <summary>
/// 뒤끝 CDN에서 MONSTER_ELEMENT_STAT_DATA 로드.
/// monster_id 로 조회 — 몬스터별 HP/공격/방어/원소/누적치 서버 수치.
/// SO 로딩 후 MonsterBase 가 이 데이터로 수치 오버라이드.
/// </summary>
public class ServerMonsterStatDataManager
{
    private const string DataFileName = "monster_element_stat_data.json";
    private string FilePath => Path.Combine(Application.persistentDataPath, DataFileName);

    private readonly Dictionary<string, MonsterElementStatEntry> _byId = new();

    public bool IsInitialized { get; private set; }

    // ── 초기화 ──────────────────────────────────

    public async UniTask InitializeAsync()
    {
        if (File.Exists(FilePath)) LoadFromJson();

        try { await LoadFromServerAsync(); }
        catch (Exception e) { Debug.LogWarning($"[ServerMonsterStatDataManager] CDN 예외: {e.Message}"); }

        if (_byId.Count == 0)
        {
            Debug.Log("[ServerMonsterStatDataManager] CDN 실패 — Resources 폴백");
            var textAsset = Resources.Load<TextAsset>("MONSTER_ELEMENT_STAT_DATA");
            if (textAsset != null)
            {
                var col = JsonUtility.FromJson<MonsterElementStatEntryCollection>(textAsset.text);
                if (col?.monsters != null)
                    foreach (var e in col.monsters) Register(e);
            }
        }

        IsInitialized = true;
        Debug.Log($"[ServerMonsterStatDataManager] 초기화 완료. 몬스터 {_byId.Count}종");
    }

    // ── 조회 ──────────────────────────────────

    public MonsterElementStatEntry GetById(string monsterId)
    {
        if (string.IsNullOrEmpty(monsterId)) return null;
        _byId.TryGetValue(monsterId, out var entry);
        return entry;
    }

    public IReadOnlyDictionary<string, MonsterElementStatEntry> GetAll() => _byId;

    // ── 내부 ──────────────────────────────────

    private void Register(MonsterElementStatEntry entry)
    {
        if (string.IsNullOrEmpty(entry?.monster_id)) return;
        _byId[entry.monster_id] = entry;
    }

    private void LoadFromJson()
    {
        try
        {
            var json = File.ReadAllText(FilePath);
            var col  = JsonUtility.FromJson<MonsterElementStatEntryCollection>(json);
            if (col?.monsters == null) return;
            _byId.Clear();
            foreach (var e in col.monsters) Register(e);
        }
        catch (Exception e) { Debug.LogError($"[ServerMonsterStatDataManager] 로컬 로드 실패: {e.Message}"); }
    }

    private void SaveToJson()
    {
        var col = new MonsterElementStatEntryCollection { monsters = new List<MonsterElementStatEntry>(_byId.Values) };
        File.WriteAllText(FilePath, JsonUtility.ToJson(col, true));
    }

    private async UniTask LoadFromServerAsync()
    {
        int loaded = ChartLoader.Load("MONSTER_ELEMENT_STAT_DATA", row =>
        {
            var entry = ParseRow(row);
            if (entry == null) return;
            if (_byId.TryGetValue(entry.monster_id, out var existing) && entry.stat_version <= existing.stat_version)
                return;
            Register(entry);
        });

        if (loaded > 0) SaveToJson();

        await UniTask.CompletedTask;
    }

    private static MonsterElementStatEntry ParseRow(JsonData row)
    {
        try
        {
            return new MonsterElementStatEntry
            {
                monster_id       = row.TryGetString("monster_id"),
                monster_name     = row.TryGetString("monster_name"),
                grade            = row.TryGetString("grade"),
                attack_type      = row.TryGetString("attack_type"),
                element          = row.TryGetString("element"),
                max_hp           = row.TryGetInt("max_hp"),
                base_attack      = row.TryGetFloat("base_attack"),
                base_defense     = row.TryGetFloat("base_defense"),
                max_accumulation = row.TryGetFloat("max_accumulation"),
                stat_version     = row.TryGetInt("stat_version"),
            };
        }
        catch { return null; }
    }
}
