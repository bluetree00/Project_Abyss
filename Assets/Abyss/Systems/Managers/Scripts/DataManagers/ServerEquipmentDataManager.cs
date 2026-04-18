using BackEnd;
using UnityEngine;
using System.Collections.Generic;
using System.IO;
using Cysharp.Threading.Tasks;
using System;
using LitJson;

/// <summary>
/// 뒤끝 CDN에서 EQUIPMENT_DATA 로드.
/// 기존 EquipmentDataManager와 분리 — 서버 장비 스탯 전용.
/// </summary>
public class ServerEquipmentDataManager
{
    private const string DataFileName = "equipment_data.json";
    private string FilePath => Path.Combine(Application.persistentDataPath, DataFileName);

    private const string ChartId = "236844";

    private Dictionary<string, EquipmentEntry> _byId = new();
    private Dictionary<string, List<EquipmentEntry>> _byType = new();

    public bool IsInitialized { get; private set; }

    // ── 초기화 ──────────────────────────────────

    public async UniTask InitializeAsync()
    {
        if (File.Exists(FilePath)) LoadFromJson();

        try { await LoadFromServerAsync(); }
        catch (Exception e) { Debug.LogWarning($"[ServerEquipmentDataManager] CDN 예외: {e.Message}"); }

        // 0건이면 Resources 폴백
        if (_byId.Count == 0)
        {
            Debug.Log("[ServerEquipmentDataManager] CDN 실패 — Resources 폴백");
            var textAsset = Resources.Load<TextAsset>("EQUIPMENT_DATA");
            if (textAsset != null)
            {
                var col = JsonUtility.FromJson<EquipmentEntryCollection>(textAsset.text);
                if (col?.equipments != null)
                    foreach (var e in col.equipments) Register(e);
            }
        }

        IsInitialized = true;
        Debug.Log($"[ServerEquipmentDataManager] 초기화 완료. 장비 {_byId.Count}개");
    }

    // ── 조회 ──────────────────────────────────

    public EquipmentEntry GetById(string weaponId)
    {
        _byId.TryGetValue(weaponId, out var entry);
        return entry;
    }

    public List<EquipmentEntry> GetByType(string weaponType)
    {
        _byType.TryGetValue(weaponType, out var list);
        return list;
    }

    public IReadOnlyDictionary<string, EquipmentEntry> GetAll() => _byId;

    // ── 내부 ──────────────────────────────────

    private void Register(EquipmentEntry entry)
    {
        if (string.IsNullOrEmpty(entry.weapon_id)) return;
        _byId[entry.weapon_id] = entry;

        if (!string.IsNullOrEmpty(entry.weapon_type))
        {
            if (!_byType.TryGetValue(entry.weapon_type, out var list))
            {
                list = new List<EquipmentEntry>();
                _byType[entry.weapon_type] = list;
            }
            list.RemoveAll(e => e.weapon_id == entry.weapon_id);
            list.Add(entry);
        }
    }

    private void LoadFromJson()
    {
        try
        {
            var json = File.ReadAllText(FilePath);
            var col = JsonUtility.FromJson<EquipmentEntryCollection>(json);
            if (col?.equipments == null) return;
            _byId.Clear();
            _byType.Clear();
            foreach (var e in col.equipments) Register(e);
        }
        catch (Exception e) { Debug.LogError($"[ServerEquipmentDataManager] 로컬 로드 실패: {e.Message}"); }
    }

    private void SaveToJson()
    {
        var col = new EquipmentEntryCollection { equipments = new List<EquipmentEntry>(_byId.Values) };
        File.WriteAllText(FilePath, JsonUtility.ToJson(col, true));
    }

    private async UniTask LoadFromServerAsync()
    {
        int loaded = ChartLoader.Load("EQUIPMENT_DATA", row =>
        {
            var entry = ParseRow(row);
            if (entry == null) return;
            if (_byId.TryGetValue(entry.weapon_id, out var existing) && entry.stat_version <= existing.stat_version)
                return;
            Register(entry);
        });

        if (loaded > 0) SaveToJson();

        await UniTask.CompletedTask;
    }

    private static EquipmentEntry ParseRow(JsonData row)
    {
        try
        {
            return new EquipmentEntry
            {
                weapon_id         = row.TryGetString("weapon_id"),
                weapon_name       = row.TryGetString("weapon_name"),
                weapon_type       = row.TryGetString("weapon_type"),
                tier              = row.TryGetInt("tier"),
                base_attack       = row.TryGetFloat("base_attack"),
                base_defense      = row.TryGetFloat("base_defense"),
                crit_chance       = row.TryGetFloat("crit_chance"),
                attack_speed      = row.TryGetFloat("attack_speed"),
                attack_range      = row.TryGetFloat("attack_range"),
                area_of_effect    = row.TryGetFloat("area_of_effect"),
                ground_combo_count= row.TryGetInt("ground_combo_count"),
                air_combo_count   = row.TryGetInt("air_combo_count"),
                hold_threshold    = row.TryGetFloat("hold_threshold"),
                promote_mode      = row.TryGetString("promote_mode"),
                charge_stages     = row.TryGetInt("charge_stages"),
                weapon_prefab_key = row.TryGetString("weapon_prefab_key"),
                weapon_display_key= row.TryGetString("weapon_display_key"),
                icon_key          = row.TryGetString("icon_key"),
                skill_q_name      = row.TryGetString("skill_q_name"),
                skill_q_cooldown  = row.TryGetFloat("skill_q_cooldown"),
                skill_e_name      = row.TryGetString("skill_e_name"),
                skill_e_cooldown  = row.TryGetFloat("skill_e_cooldown"),

                // 원소
                element                  = row.TryGetString("element"),
                element_amount_basic     = row.TryGetFloat("element_amount_basic"),
                element_amount_heavy     = row.TryGetFloat("element_amount_heavy"),
                element_amount_air       = row.TryGetFloat("element_amount_air"),
                skill_q_element_override = row.TryGetString("skill_q_element_override"),
                skill_q_element_amount   = row.TryGetFloat("skill_q_element_amount"),
                skill_e_element_override = row.TryGetString("skill_e_element_override"),
                skill_e_element_amount   = row.TryGetFloat("skill_e_element_amount"),

                stat_version      = row.TryGetInt("stat_version"),
            };
        }
        catch { return null; }
    }
}
