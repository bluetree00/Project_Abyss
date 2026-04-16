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

    /// <summary>캐시 초기화.</summary>
    public static void Clear() => _map.Clear();
}
