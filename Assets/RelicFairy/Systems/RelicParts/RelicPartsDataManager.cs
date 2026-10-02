using System;
using System.Collections.Generic;
using System.IO;
using Cysharp.Threading.Tasks;
using LitJson;
using UnityEngine;

/// <summary>
/// 뒤끝 CDN에서 RELIC_PARTS_DATA 차트를 로드한다(유물 기억 조각 — 유물 성장 v2).
///
/// 로드 우선순위: 로컬 캐시 JSON → CDN → Addressables 폴백(오프라인).
/// RelicAwakeningDataManager와 동일한 패턴.
///
/// <b>스키마 문(10-02 v2)</b>: 매달리는 자리(<c>anchor</c>)가 없는 행은 v1 파츠라 버린다.
/// CDN에 옛 14행 차트가 그대로 남아 있어도 프로젝트 JSON(42행)으로 떨어지게 하려는 것 — 새 CSV를 올리면 그때부터 CDN이 이긴다.
/// 컬럼은 <see cref="RelicPartEntry"/> 머리말.
/// </summary>
public sealed class RelicPartsDataManager
{
    private const string ChartName    = "RELIC_PARTS_DATA";
    private const string DataFileName = "relic_parts_data.json";

    private string FilePath => Path.Combine(Application.persistentDataPath, DataFileName);

    // part_id → entry (전체 조회)
    private readonly Dictionary<string, RelicPartEntry> _byId = new();
    // relic_id → entries (드래프트 풀 필터용)
    private readonly Dictionary<string, List<RelicPartEntry>> _byRelic = new();

    public bool IsInitialized { get; private set; }

    /// <summary>스키마 문이 버린 옛(v1) 행 수 — 실측 · 로그용.</summary>
    public int RejectedLegacyRows { get; private set; }

    // ── 초기화 ──────────────────────────────────────────────────────────────

    public async UniTask InitializeAsync()
    {
        if (File.Exists(FilePath)) LoadFromJson();

        try { await LoadFromServerAsync(); }
        catch (Exception e) { Debug.LogWarning($"[RelicPartsDataManager] CDN 예외: {e.Message}"); }

        if (_byId.Count == 0)
        {
            Debug.Log("[RelicPartsDataManager] CDN 실패 — Addressables 폴백");
            var textAsset = await Managers.AddressableManager.TryLoadAssetAsync<TextAsset>(ChartName);
            if (textAsset != null)
            {
                var col = JsonUtility.FromJson<RelicPartEntryCollection>(textAsset.text);
                if (col?.entries != null)
                    foreach (var e in col.entries) Register(e);
            }
        }

        IsInitialized = true;
        Debug.Log($"[RelicPartsDataManager] 초기화 완료. {_byId.Count}개 파츠 로드");
    }

    // ── 조회 ────────────────────────────────────────────────────────────────

    /// <summary>part_id로 단일 파츠 조회. 없으면 null.</summary>
    public RelicPartEntry GetById(string partId)
    {
        if (string.IsNullOrEmpty(partId)) return null;
        return _byId.TryGetValue(partId, out var e) ? e : null;
    }

    /// <summary>유물 하나의 조각 전부(데이터 순서). 없으면 빈 목록.</summary>
    public IReadOnlyList<RelicPartEntry> GetByRelic(string relicId)
        => !string.IsNullOrEmpty(relicId) && _byRelic.TryGetValue(relicId, out var list) ? list : (IReadOnlyList<RelicPartEntry>)System.Array.Empty<RelicPartEntry>();

    /// <summary>
    /// [옛 v1] 드래프트 후보 풀 — v2 카드는 <see cref="RelicDraftComposer"/>가 만든다. 실측 도구 호환으로만 남는다.
    /// 해당 유물의 파츠 중 boss_tier가 일치하고, 이미 보유하지 않았으며,
    /// 선행 파츠(requires) 요구를 충족한 것. bossTier 1 = 기능 파츠, 3 = 코어 진화(데이터 boss_tier).
    /// requires는 CSV 컬럼 — 값이 있으면 그 part_id를 보유해야 후보에 오른다(죽은 픽 방지).
    /// </summary>
    public List<RelicPartEntry> GetDraftPool(string relicId, int bossTier, IReadOnlyList<string> ownedPartIds)
    {
        var result = new List<RelicPartEntry>();
        if (string.IsNullOrEmpty(relicId) || !_byRelic.TryGetValue(relicId, out var list)) return result;

        foreach (var e in list)
        {
            if (e.boss_tier != bossTier) continue;
            if (Owns(ownedPartIds, e.part_id)) continue;
            // 선행 파츠 미보유면 제외(죽은 픽 방지) — requires 빈칸이면 항상 통과.
            if (!string.IsNullOrEmpty(e.requires) && !Owns(ownedPartIds, e.requires)) continue;
            result.Add(e);
        }
        return result;
    }

    // ── 내부 ────────────────────────────────────────────────────────────────

    private static bool Owns(IReadOnlyList<string> ownedPartIds, string partId)
    {
        if (ownedPartIds == null) return false;
        for (int i = 0; i < ownedPartIds.Count; i++)
            if (ownedPartIds[i] == partId) return true;
        return false;
    }

    private void Register(RelicPartEntry entry)
    {
        if (entry == null || string.IsNullOrEmpty(entry.part_id) || string.IsNullOrEmpty(entry.relic_id)) return;
        if (!entry.IsV2) { RejectedLegacyRows++; return; }   // 스키마 문 — v1 행은 버린다

        _byId[entry.part_id] = entry;

        if (!_byRelic.TryGetValue(entry.relic_id, out var list))
        {
            list = new List<RelicPartEntry>();
            _byRelic[entry.relic_id] = list;
        }
        list.Add(entry);
    }

    private void ClearAll()
    {
        _byId.Clear();
        _byRelic.Clear();
        RejectedLegacyRows = 0;
    }

    private void LoadFromJson()
    {
        try
        {
            var json = File.ReadAllText(FilePath);
            var col  = JsonUtility.FromJson<RelicPartEntryCollection>(json);
            if (col?.entries == null) return;

            ClearAll();
            foreach (var e in col.entries) Register(e);
        }
        catch (Exception e) { Debug.LogError($"[RelicPartsDataManager] 로컬 로드 실패: {e.Message}"); }
    }

    private void SaveToJson()
    {
        var all = new List<RelicPartEntry>(_byId.Values);
        var col = new RelicPartEntryCollection { entries = all };
        File.WriteAllText(FilePath, JsonUtility.ToJson(col, true));
    }

    private async UniTask LoadFromServerAsync()
    {
        ClearAll();

        ChartLoader.Load(ChartName, row =>
        {
            var entry = ParseRow(row);
            if (entry != null) Register(entry);
        });

        // v2 행이 하나라도 들어왔을 때만 캐시를 갈아 끼운다 — 옛 CDN만 있으면 v2 캐시를 지우지 않는다.
        if (_byId.Count > 0) SaveToJson();
        else if (RejectedLegacyRows > 0)
            Debug.Log($"[RelicPartsDataManager] CDN 행 {RejectedLegacyRows}개가 옛 스키마(anchor 없음) — 프로젝트 JSON으로 간다");

        await UniTask.CompletedTask;
    }

    private static RelicPartEntry ParseRow(JsonData row)
    {
        try
        {
            var partId = row.TryGetString("part_id");
            if (string.IsNullOrEmpty(partId)) return null;

            return new RelicPartEntry
            {
                index       = row.TryGetInt("index"),
                relic_id    = row.TryGetString("relic_id"),
                part_kind   = row.TryGetString("part_kind"),
                part_id     = partId,
                part_name   = row.TryGetString("part_name"),
                description = row.TryGetString("description"),
                effect_key  = row.TryGetString("effect_key"),
                boss_tier   = row.TryGetInt("boss_tier"),
                requires    = row.TryGetString("requires"),   // 컬럼 없으면 빈 문자열(선행조건 없음)
                anchor              = row.TryGetString("anchor"),
                rune_element        = row.TryGetString("rune_element"),
                line1               = row.TryGetString("line1"),
                line2               = row.TryGetString("line2"),
                line3               = row.TryGetString("line3"),
                memory_line         = row.TryGetString("memory_line"),
                memory_line_radiant = row.TryGetString("memory_line_radiant"),
                build_family        = row.TryGetString("build_family"),
            };
        }
        catch { return null; }
    }
}
