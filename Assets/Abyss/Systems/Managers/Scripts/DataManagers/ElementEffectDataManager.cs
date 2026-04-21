using System;
using System.Collections.Generic;
using System.IO;
using Cysharp.Threading.Tasks;
using LitJson;
using UnityEngine;

/// <summary>
/// 뒤끝 CDN에서 ELEMENT_EFFECT_DATA 로드.
/// element 문자열(Fire/Water/Grass/Earth/Lightning)로 조회.
/// </summary>
public class ElementEffectDataManager
{
    private const string DataFileName = "element_effect_data.json";
    private string FilePath => Path.Combine(Application.persistentDataPath, DataFileName);

    private readonly Dictionary<string, ElementEffectEntry> _byElement = new();

    public bool IsInitialized { get; private set; }

    // ── 초기화 ──────────────────────────────────

    public async UniTask InitializeAsync()
    {
        if (File.Exists(FilePath)) LoadFromJson();

        try { await LoadFromServerAsync(); }
        catch (Exception e) { Debug.LogWarning($"[ElementEffectDataManager] CDN 예외: {e.Message}"); }

        if (_byElement.Count == 0)
        {
            Debug.Log("[ElementEffectDataManager] CDN 실패 — Resources 폴백");
            var textAsset = Resources.Load<TextAsset>("ELEMENT_EFFECT_DATA");
            if (textAsset != null)
            {
                var col = JsonUtility.FromJson<ElementEffectEntryCollection>(textAsset.text);
                if (col?.effects != null)
                    foreach (var e in col.effects) Register(e);
            }
        }

        IsInitialized = true;
        Debug.Log($"[ElementEffectDataManager] 초기화 완료. 원소 효과 {_byElement.Count}개");

        // 연결 검증용 — 각 원소 주요 수치 요약
        foreach (var kv in _byElement)
        {
            var e = kv.Value;
            Debug.Log($"  · {e.element,-10} | {e.effect_type,-16} dur={e.duration:F1}s mA={e.magnitude_a:F2} mB={e.magnitude_b:F2} tick={e.tick_interval:F1} gauge={e.activation_gauge:F0}");
        }
    }

    // ── 조회 ──────────────────────────────────

    /// <summary>원소 이름(Fire/Water/Grass/Earth/Lightning)으로 조회.</summary>
    public ElementEffectEntry Get(string element)
    {
        _byElement.TryGetValue(element, out var entry);
        return entry;
    }

    /// <summary>ElementType enum으로 조회.</summary>
    public ElementEffectEntry Get(ElementType element)
    {
        return element.IsValid() ? Get(element.ToString()) : null;
    }

    public IReadOnlyDictionary<string, ElementEffectEntry> GetAll() => _byElement;

    // ── 내부 ──────────────────────────────────

    private void Register(ElementEffectEntry entry)
    {
        if (string.IsNullOrEmpty(entry?.element)) return;
        _byElement[entry.element] = entry;
    }

    private void LoadFromJson()
    {
        try
        {
            var json = File.ReadAllText(FilePath);
            var col  = JsonUtility.FromJson<ElementEffectEntryCollection>(json);
            if (col?.effects == null) return;
            _byElement.Clear();
            foreach (var e in col.effects) Register(e);
        }
        catch (Exception e) { Debug.LogError($"[ElementEffectDataManager] 로컬 로드 실패: {e.Message}"); }
    }

    private void SaveToJson()
    {
        var col = new ElementEffectEntryCollection { effects = new List<ElementEffectEntry>(_byElement.Values) };
        File.WriteAllText(FilePath, JsonUtility.ToJson(col, true));
    }

    private async UniTask LoadFromServerAsync()
    {
        _byElement.Clear();

        int loaded = ChartLoader.Load("ELEMENT_EFFECT_DATA", row =>
        {
            var entry = ParseRow(row);
            if (entry != null) Register(entry);
        });

        if (loaded > 0) SaveToJson();

        await UniTask.CompletedTask;
    }

    private static ElementEffectEntry ParseRow(JsonData row)
    {
        try
        {
            return new ElementEffectEntry
            {
                element             = row.TryGetString("element"),
                effect_id           = row.TryGetString("effect_id"),
                effect_type         = row.TryGetString("effect_type"),
                duration            = row.TryGetFloat("duration"),
                magnitude_a         = row.TryGetFloat("magnitude_a"),
                magnitude_b         = row.TryGetFloat("magnitude_b"),
                tick_interval       = row.TryGetFloat("tick_interval"),
                counter_multiplier  = row.TryGetFloat("counter_multiplier"),
                reverse_multiplier  = row.TryGetFloat("reverse_multiplier"),
                same_multiplier     = row.TryGetFloat("same_multiplier"),
                activation_gauge    = row.TryGetFloat("Activation_gauge"),
                vfx_key             = row.TryGetString("vfx_key"),
                sfx_key             = row.TryGetString("sfx_key"),
                description         = row.TryGetString("description"),
                stat_version        = row.TryGetInt("stat_version"),
            };
        }
        catch { return null; }
    }
}
