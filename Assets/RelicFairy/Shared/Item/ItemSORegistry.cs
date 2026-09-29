using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// ItemSO를 itemId로 조회하는 정적 레지스트리.
/// ItemSODatabase SO에서 초기화되거나, 런타임에 직접 등록.
/// SO가 없는 아이템은 null 반환 — CSV만으로 동작.
/// </summary>
public static class ItemSORegistry
{
    private static readonly Dictionary<string, ItemSO> _map = new();
    private static bool _warnedMissingChart;

    /// <summary>itemId로 SO 조회. 없으면 null.</summary>
    public static ItemSO Find(string itemId)
    {
        if (string.IsNullOrEmpty(itemId)) return null;
        _map.TryGetValue(itemId, out var so);
        return so;
    }

    /// <summary>SO 등록.</summary>
    public static void Register(ItemSO so)
    {
        if (so == null || string.IsNullOrEmpty(so.itemId)) return;
        _map[so.itemId] = so;
    }

    /// <summary>여러 SO 일괄 등록.</summary>
    public static void RegisterAll(IEnumerable<ItemSO> items)
    {
        foreach (var so in items)
            Register(so);
    }

    /// <summary>등록 수.</summary>
    public static int Count => _map.Count;

    /// <summary>등록된 모든 ItemSO 열거 (호출자는 readonly로 사용).</summary>
    public static IEnumerable<ItemSO> All => _map.Values;

    /// <summary>
    /// 지정 등급에 해당하는 ItemSO 후보 리스트 반환. 매번 새 리스트를 생성한다.
    /// <para>효과의 정본은 차트(ITEM_DATA)다 — 차트가 모르는 SO(새 룬을 만든 뒤 CDN 업로드 전)는 효과 없는 빈 룬이 되므로 뺀다.
    /// 차트가 아직 비어 있으면(불러오기 전 · 실패) 거르지 않는다.</para>
    /// </summary>
    public static List<ItemSO> GetByRarity(ItemRarity rarity)
    {
        var chart = Managers.ItemData;
        bool gate = chart != null && chart.IsInitialized && chart.GetAllItems().Count > 0;
        int skipped = 0;

        var result = new List<ItemSO>(_map.Count);
        foreach (var so in _map.Values)
        {
            if (so == null || so.rarity != rarity) continue;
            if (gate && chart.GetItem(so.itemId) == null) { skipped++; continue; }
            result.Add(so);
        }

        if (skipped > 0 && !_warnedMissingChart)
        {
            _warnedMissingChart = true;
            Debug.LogWarning($"[ItemSORegistry] 차트(ITEM_DATA)에 없는 룬 SO {skipped}종({rarity})을 제시 풀에서 뺐다 — CDN ITEM_DATA 업로드가 필요하다.");
        }
        return result;
    }

    /// <summary>캐시 초기화.</summary>
    public static void Clear() => _map.Clear();
}
