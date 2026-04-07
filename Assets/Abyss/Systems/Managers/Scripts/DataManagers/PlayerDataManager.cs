using BackEnd;
using UnityEngine;
using System.Collections.Generic;
using System.IO;
using Cysharp.Threading.Tasks;
using System;
using LitJson;

/// <summary>
/// 뒤끝 CDN에서 PLAYER_DATA + PASSIVE_DATA 로드.
/// MonsterDataManager와 동일 패턴.
/// </summary>
public class PlayerDataManager
{
    private const string PlayerDataFileName = "player_data.json";
    private const string PassiveDataFileName = "passive_data.json";
    private string PlayerFilePath => Path.Combine(Application.persistentDataPath, PlayerDataFileName);
    private string PassiveFilePath => Path.Combine(Application.persistentDataPath, PassiveDataFileName);

    private const string PlayerChartId = "234858";
    private const string PassiveChartId = "234857";

    private Dictionary<string, PlayerStatEntry> _playerById = new();
    private Dictionary<string, List<PassiveEntry>> _passiveById = new();

    public bool IsInitialized { get; private set; }

    // ── 초기화 ──────────────────────────────────

    public async UniTask InitializeAsync()
    {
        // 로컬 로드
        if (File.Exists(PlayerFilePath)) LoadPlayersFromJson();
        if (File.Exists(PassiveFilePath)) LoadPassivesFromJson();

        // 서버 갱신
        try { await LoadFromServerAsync(); }
        catch (Exception e) { Debug.LogWarning($"[PlayerDataManager] CDN 예외: {e.Message}"); }

        // 0건이면 Resources 폴백
        if (_playerById.Count == 0)
        {
            Debug.Log("[PlayerDataManager] CDN 실패 — Resources 폴백");
            var playerJson = Resources.Load<TextAsset>("PLAYER_DATA");
            if (playerJson != null)
            {
                var col = JsonUtility.FromJson<PlayerStatEntryCollection>(playerJson.text);
                if (col?.players != null)
                    foreach (var p in col.players) _playerById[p.char_id] = p;
            }
            var passiveJson = Resources.Load<TextAsset>("PASSIVE_DATA");
            if (passiveJson != null)
            {
                var col = JsonUtility.FromJson<PassiveEntryCollection>(passiveJson.text);
                if (col?.passives != null)
                    foreach (var p in col.passives)
                    {
                        if (string.IsNullOrEmpty(p.effect_type)) continue;
                        if (!_passiveById.TryGetValue(p.passive_id, out var list))
                        {
                            list = new List<PassiveEntry>();
                            _passiveById[p.passive_id] = list;
                        }
                        list.Add(p);
                    }
            }
        }

        IsInitialized = true;
        Debug.Log($"[PlayerDataManager] 초기화 완료. 캐릭터 {_playerById.Count}명, 패시브 그룹 {_passiveById.Count}개");
    }

    // ── 조회 ──────────────────────────────────

    public PlayerStatEntry GetPlayer(string charId)
    {
        _playerById.TryGetValue(charId, out var entry);
        return entry;
    }

    public List<PassiveEntry> GetPassives(string passiveId)
    {
        _passiveById.TryGetValue(passiveId, out var list);
        return list;
    }

    public IReadOnlyDictionary<string, PlayerStatEntry> GetAllPlayers() => _playerById;

    // ── 로컬 저장/로드 ──────────────────────────

    private void LoadPlayersFromJson()
    {
        try
        {
            var json = File.ReadAllText(PlayerFilePath);
            var col = JsonUtility.FromJson<PlayerStatEntryCollection>(json);
            if (col?.players == null) return;
            _playerById.Clear();
            foreach (var p in col.players) _playerById[p.char_id] = p;
        }
        catch (Exception e) { Debug.LogError($"[PlayerDataManager] 로컬 로드 실패: {e.Message}"); }
    }

    private void LoadPassivesFromJson()
    {
        try
        {
            var json = File.ReadAllText(PassiveFilePath);
            var col = JsonUtility.FromJson<PassiveEntryCollection>(json);
            if (col?.passives == null) return;
            _passiveById.Clear();
            foreach (var p in col.passives)
            {
                if (string.IsNullOrEmpty(p.passive_id) || string.IsNullOrEmpty(p.effect_type)) continue;
                if (!_passiveById.TryGetValue(p.passive_id, out var list))
                {
                    list = new List<PassiveEntry>();
                    _passiveById[p.passive_id] = list;
                }
                list.Add(p);
            }
        }
        catch (Exception e) { Debug.LogError($"[PlayerDataManager] 패시브 로컬 로드 실패: {e.Message}"); }
    }

    private void SavePlayersToJson()
    {
        var col = new PlayerStatEntryCollection { players = new List<PlayerStatEntry>(_playerById.Values) };
        File.WriteAllText(PlayerFilePath, JsonUtility.ToJson(col, true));
    }

    private void SavePassivesToJson()
    {
        var all = new List<PassiveEntry>();
        foreach (var list in _passiveById.Values) all.AddRange(list);
        var col = new PassiveEntryCollection { passives = all };
        File.WriteAllText(PassiveFilePath, JsonUtility.ToJson(col, true));
    }

    // ── 서버 로드 ──────────────────────────────

    private async UniTask LoadFromServerAsync()
    {
        var tableResult = Backend.CDN.Content.Table.Get();
        if (!tableResult.IsSuccess())
        {
            Debug.LogWarning($"[PlayerDataManager] 차트 테이블 조회 실패: {tableResult.GetStatusCode()}");
            return;
        }

        var contentResult = Backend.CDN.Content.Get(tableResult.GetContentTableItemList());
        if (!contentResult.IsSuccess())
        {
            Debug.LogWarning($"[PlayerDataManager] 차트 다운로드 실패");
            return;
        }

        Backend.CDN.Content.Local.Save(contentResult.GetContentList(), out _);
        var localResult = Backend.CDN.Content.Local.Load();
        if (!localResult.IsSuccess())
        {
            Debug.LogWarning("[PlayerDataManager] 로컬 로드 실패");
            return;
        }

        var dic = localResult.GetContentDictionarySortByChartId();

        // Player 데이터
        if (dic.ContainsKey(PlayerChartId))
        {
            var json = LitJson.JsonMapper.ToObject(dic[PlayerChartId].contentJson.ToString());
            int count = 0;
            foreach (LitJson.JsonData row in json)
            {
                var entry = ParsePlayerRow(row);
                if (entry == null) continue;
                if (_playerById.TryGetValue(entry.char_id, out var existing) && entry.stat_version <= existing.stat_version)
                    continue;
                _playerById[entry.char_id] = entry;
                count++;
            }
            SavePlayersToJson();
            Debug.Log($"[PlayerDataManager] 캐릭터 {count}개 갱신");
        }

        // Passive 데이터
        if (dic.ContainsKey(PassiveChartId))
        {
            var json = LitJson.JsonMapper.ToObject(dic[PassiveChartId].contentJson.ToString());
            int count = 0;
            foreach (LitJson.JsonData row in json)
            {
                var entry = ParsePassiveRow(row);
                if (entry == null || string.IsNullOrEmpty(entry.effect_type)) continue;

                if (!_passiveById.TryGetValue(entry.passive_id, out var list))
                {
                    list = new List<PassiveEntry>();
                    _passiveById[entry.passive_id] = list;
                }
                // 슬롯 중복 제거
                list.RemoveAll(p => p.slot == entry.slot);
                list.Add(entry);
                count++;
            }
            SavePassivesToJson();
            Debug.Log($"[PlayerDataManager] 패시브 {count}개 갱신");
        }

        await UniTask.CompletedTask;
    }

    private static PlayerStatEntry ParsePlayerRow(JsonData row)
    {
        try
        {
            return new PlayerStatEntry
            {
                char_id               = row.TryGetString("char_id"),
                char_name             = row.TryGetString("char_name"),
                @class                = row.TryGetString("class"),
                max_health            = row.TryGetInt("max_health"),
                base_melee_attack     = row.TryGetInt("base_melee_attack"),
                base_ranged_attack    = row.TryGetInt("base_ranged_attack"),
                base_defense          = row.TryGetInt("base_defense"),
                base_luck             = row.TryGetInt("base_luck"),
                base_move_speed       = row.TryGetFloat("base_move_speed"),
                base_run_speed        = row.TryGetFloat("base_run_speed"),
                combo_duration        = row.TryGetFloat("combo_duration"),
                heavy_charge_threshold= row.TryGetFloat("heavy_charge_threshold"),
                heavy_release_time    = row.TryGetFloat("heavy_release_time"),
                dash_speed            = row.TryGetFloat("dash_speed"),
                dash_duration         = row.TryGetFloat("dash_duration"),
                dodge_cooldown        = row.TryGetFloat("dodge_cooldown"),
                jump_force            = row.TryGetFloat("jump_force"),
                gravity               = row.TryGetFloat("gravity"),
                fall_multiplier       = row.TryGetFloat("fall_multiplier"),
                ground_check_distance = row.TryGetFloat("ground_check_distance"),
                air_control_multiplier= row.TryGetFloat("air_control_multiplier"),
                ground_drag           = row.TryGetFloat("ground_drag"),
                air_drag              = row.TryGetFloat("air_drag"),
                passive_id            = row.TryGetString("passive_id"),
                stat_version          = row.TryGetInt("stat_version"),
            };
        }
        catch { return null; }
    }

    private static PassiveEntry ParsePassiveRow(JsonData row)
    {
        try
        {
            return new PassiveEntry
            {
                passive_id  = row.TryGetString("passive_id"),
                slot        = row.TryGetInt("slot"),
                effect_type = row.TryGetString("effect_type"),
                trigger     = row.TryGetString("trigger"),
                value       = row.TryGetFloat("value"),
                max_stack   = row.TryGetInt("max_stack"),
                duration    = row.TryGetFloat("duration"),
                description = row.TryGetString("description"),
                stat_version= row.TryGetInt("stat_version"),
            };
        }
        catch { return null; }
    }
}
