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

    public void AddItem(RuntimeItemData item)
    {
        if (item == null) return;
        if (HasItem(item.itemId)) return; // 중복 방지
        _items.Add(item);
        OnInventoryChanged?.Invoke();
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
