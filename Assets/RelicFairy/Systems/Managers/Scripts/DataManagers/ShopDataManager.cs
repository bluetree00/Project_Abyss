using Cysharp.Threading.Tasks;
using LitJson;
using System;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;

/// <summary>
/// 뒤끝 CDN에서 SHOP_PRICE_DATA 차트를 로드하고, 상점 후보 풀과 가격 결정 로직을 제공.
/// - shop_entry_id로 PK 조회
/// - (category, rarity)로 사전 인덱싱된 풀 조회 (LuckRollService 등에서 사용)
/// - ResolvePrice: price_override > 0이면 그 값, 아니면 ShopPriceTableSO 기본가
///
/// rarity 폴백 정책:
/// - BuildIndexes 단계에서 target_id의 rarity 조회 실패 시 → 풀에 추가하지 않음 + warning log.
///   (잘못된 항목이 추첨 풀에 들어가면 후속 단계에서 nullref가 나므로 격리.)
/// - ResolvePrice의 LookupRarity 실패 시 → Common 폴백 + warning log.
///   (방어적 처리; 정상 흐름에서는 풀에 들어간 entry만 가격을 묻기 때문에 발생하지 않아야 한다.)
/// </summary>
public class ShopDataManager
{
    private const string ChartName    = "SHOP_PRICE_DATA";
    private const string CategoryItem   = "item";
    private const string CategoryWeapon = "weapon";

    private readonly List<ShopEntry> _all = new();
    private readonly Dictionary<string, ShopEntry> _byId = new();
    private readonly Dictionary<(string category, ItemRarity rarity), List<ShopEntry>> _byCatRarity = new();

    private ShopPriceTableSO _priceTable;
    private bool _initialized;

    public bool IsInitialized => _initialized;
    public IReadOnlyList<ShopEntry> All => _all;
    public ShopPriceTableSO PriceTable => _priceTable;

    // ── 초기화 ──────────────────────────────────

    /// <summary>
    /// SHOP_PRICE_DATA 로드 + 인덱싱.
    /// priceTable이 null이면 ResolvePrice는 항상 0 반환 (price_override만 살아있음).
    /// </summary>
    public async UniTask InitializeAsync(ShopPriceTableSO priceTable, CancellationToken ct = default)
    {
        try
        {
            _priceTable = priceTable;

            ct.ThrowIfCancellationRequested();

            int loaded = ChartLoader.Load(ChartName, row =>
            {
                var entry = ParseRow(row);
                if (entry == null) return;
                if (string.IsNullOrEmpty(entry.shop_entry_id)) return;

                // stat_version 비교: 더 낮거나 같은 버전은 무시 (이미 등록된 항목이 더 최신)
                if (_byId.TryGetValue(entry.shop_entry_id, out var existing) && entry.stat_version <= existing.stat_version)
                    return;

                Register(entry);
            });

            ct.ThrowIfCancellationRequested();

            BuildIndexes();

            _initialized = true;
            Debug.Log($"[ShopDataManager] 초기화 완료. 차트={loaded}행, 등록={_all.Count}, 풀그룹={_byCatRarity.Count}");

            await UniTask.CompletedTask;
        }
        catch (OperationCanceledException)
        {
            Debug.Log("[ShopDataManager] 초기화 취소됨");
            throw;
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[ShopDataManager] 초기화 예외: {e.Message}");
            _initialized = true; // 부분 초기화라도 호출자가 진행 가능하도록 플래그 세움
        }
    }

    // ── 조회 ──────────────────────────────────

    public ShopEntry GetById(string shopEntryId)
    {
        if (string.IsNullOrEmpty(shopEntryId)) return null;
        _byId.TryGetValue(shopEntryId, out var entry);
        return entry;
    }

    /// <summary>
    /// (category, rarity)로 사전 인덱싱된 풀 반환.
    /// 항목이 없으면 빈 리스트 (null 아님). 반환 리스트는 외부에서 수정하지 말 것.
    /// </summary>
    public List<ShopEntry> GetPool(string category, ItemRarity rarity)
    {
        string normalized = NormalizeCategory(category);
        if (_byCatRarity.TryGetValue((normalized, rarity), out var list))
            return list;
        return new List<ShopEntry>(0);
    }

    /// <summary>
    /// entry의 가격 결정. price_override > 0이면 그 값, 아니면 SO 등급 기본가.
    /// </summary>
    public int ResolvePrice(ShopEntry entry)
    {
        if (entry == null) return 0;
        if (entry.price_override > 0) return entry.price_override;

        if (_priceTable == null)
        {
            Debug.LogWarning("[ShopDataManager] PriceTable null — ResolvePrice returns 0.");
            return 0;
        }

        if (!TryLookupRarity(entry.category, entry.target_id, out var rarity))
        {
            Debug.LogWarning($"[ShopDataManager] ResolvePrice rarity lookup 실패 (category={entry.category}, target={entry.target_id}) — Common 폴백");
            rarity = ItemRarity.Common;
        }
        return _priceTable.GetBasePrice(entry.category, rarity);
    }

    // ── 내부 ──────────────────────────────────

    private void Register(ShopEntry entry)
    {
        _byId[entry.shop_entry_id] = entry;
        // _all은 중복 시 갱신해야 하므로 제거 후 추가
        _all.RemoveAll(e => e.shop_entry_id == entry.shop_entry_id);
        _all.Add(entry);
    }

    private void BuildIndexes()
    {
        _byCatRarity.Clear();

        var skippedIds = new List<string>();
        foreach (var entry in _all)
        {
            if (string.IsNullOrEmpty(entry.category) || string.IsNullOrEmpty(entry.target_id))
            {
                skippedIds.Add(entry.shop_entry_id);
                continue;
            }

            if (!TryLookupRarity(entry.category, entry.target_id, out var rarity))
            {
                skippedIds.Add(entry.shop_entry_id);
                continue;
            }

            string normCat = NormalizeCategory(entry.category);
            var key = (normCat, rarity);
            if (!_byCatRarity.TryGetValue(key, out var list))
            {
                list = new List<ShopEntry>();
                _byCatRarity[key] = list;
            }
            list.Add(entry);
        }

        // 풀별 카운트 디버그
        var sb = new System.Text.StringBuilder();
        sb.Append("[ShopDataManager] 풀 통계: ");
        foreach (var kv in _byCatRarity)
            sb.Append($"({kv.Key.category}/{kv.Key.rarity}={kv.Value.Count}) ");
        if (skippedIds.Count > 0) sb.Append($"| skipped={skippedIds.Count}");
        Debug.Log(sb.ToString());

        // 제외 항목은 한 줄로 요약(개별 도배 방지). 상점 데이터 재제작 후 0이어야 정상.
        if (skippedIds.Count > 0)
            Debug.LogWarning($"[ShopDataManager] 풀 제외 {skippedIds.Count}건(타깃 등급 조회 실패/누락): {string.Join(", ", skippedIds)}");
    }

    /// <summary>
    /// target_id의 등급 조회.
    /// - weapon: ServerEquipmentDataManager.GetById(target_id).rarity (string) → ItemRarity 파싱
    /// - item: ItemSORegistry.Find(target_id).rarity 사용
    /// 실패 시 false.
    /// </summary>
    private static bool TryLookupRarity(string category, string targetId, out ItemRarity rarity)
    {
        rarity = ItemRarity.Common;
        if (string.IsNullOrEmpty(category) || string.IsNullOrEmpty(targetId)) return false;

        string normalized = category.ToLowerInvariant();
        if (normalized == CategoryWeapon)
        {
            var equipMgr = Managers.ServerEquipment;
            if (equipMgr == null) return false;
            var equip = equipMgr.GetById(targetId);
            if (equip == null) return false;
            return TryParseRarity(equip.rarity, out rarity);
        }

        if (normalized == CategoryItem)
        {
            var itemSO = ItemSORegistry.Find(targetId);
            if (itemSO == null) return false;
            rarity = itemSO.rarity;
            return true;
        }

        return false;
    }

    private static bool TryParseRarity(string raw, out ItemRarity rarity)
    {
        rarity = ItemRarity.Common;
        if (string.IsNullOrEmpty(raw)) return false;
        return Enum.TryParse(raw, ignoreCase: true, out rarity);
    }

    private static string NormalizeCategory(string category)
    {
        if (string.IsNullOrEmpty(category)) return string.Empty;
        return category.ToLowerInvariant();
    }

    private static ShopEntry ParseRow(JsonData row)
    {
        try
        {
            return new ShopEntry
            {
                index          = row.TryGetInt("index"),
                shop_entry_id  = row.TryGetString("shop_entry_id"),
                category       = row.TryGetString("category"),
                target_id      = row.TryGetString("target_id"),
                price_override = row.TryGetInt("price_override"),
                weight         = row.TryGetInt("weight"),
                stat_version   = row.TryGetInt("stat_version"),
            };
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[ShopDataManager] ParseRow 예외: {e.Message}");
            return null;
        }
    }
}
