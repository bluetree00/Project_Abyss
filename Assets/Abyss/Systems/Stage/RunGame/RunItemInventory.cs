using System.Collections.Generic;

/// <summary>
/// 런 중 획득한 아이템을 추적.
/// RuntimeItemData 기반 — 서버/SO 양쪽 호환.
/// </summary>
public sealed class RunItemInventory
{
    private readonly List<RuntimeItemData> _items = new();

    public event System.Action OnInventoryChanged;

    public IReadOnlyList<RuntimeItemData> Items => _items;
    public int Count => _items.Count;

    public bool HasItem(string itemId)
    {
        if (string.IsNullOrEmpty(itemId)) return false;
        foreach (var item in _items)
            if (item.itemId == itemId) return true;
        return false;
    }

    /// <summary>같은 itemId를 인벤토리에 보유한 개수. 비어있으면 0.</summary>
    public int CountItem(string itemId)
    {
        if (string.IsNullOrEmpty(itemId)) return 0;
        int count = 0;
        foreach (var item in _items)
            if (item.itemId == itemId) count++;
        return count;
    }

    /// <summary>
    /// 아이템 추가. 같은 itemId는 ItemSO.MaxStack까지 누적 허용.
    /// 가득 차면 false, 추가 성공 시 true.
    /// </summary>
    public bool AddItem(RuntimeItemData item)
    {
        if (item == null) return false;

        int maxStack = ResolveMaxStack(item);
        int currentCount = CountItem(item.itemId);

        if (currentCount >= maxStack) return false;

        _items.Add(item);
        QuestEvents.ReportItemCollect(item?.itemId ?? "Unknown");

        if (item.shapeId > 0)
            BlockSynergyBridge.Instance?.RegisterShapeFromItem(item.shapeId);

        OnInventoryChanged?.Invoke();
        return true;
    }

    /// <summary>itemId의 ItemSO.MaxStack 조회. SO 미등록 시 1로 폴백.</summary>
    private static int ResolveMaxStack(RuntimeItemData item)
    {
        if (item == null || string.IsNullOrEmpty(item.itemId)) return 1;
        var so = ItemSORegistry.Find(item.itemId);
        return so != null ? so.MaxStack : 1;
    }

    public void RemoveItem(RuntimeItemData item)
    {
        if (item == null) return;
        if (_items.Remove(item))
            OnInventoryChanged?.Invoke();
    }

    public void Clear()
    {
        _items.Clear();
        OnInventoryChanged?.Invoke();
    }
}
