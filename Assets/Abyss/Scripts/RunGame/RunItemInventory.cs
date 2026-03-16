using System.Collections.Generic;

/// <summary>
/// 런 중 획득한 아이템을 추적하고, 누적 스탯 합산을 제공
/// - 런 시작 시 생성, 종료 시 폐기 (GameRunSession이 소유)
/// - 아이템 추가 시 이벤트로 PlayerRuntimeStats에 통보
/// </summary>
public sealed class RunItemInventory
{
    private readonly List<ItemSO> _items = new List<ItemSO>();

    // 아이템 목록이 변경될 때 발생 — 구독자가 스탯을 재계산
    public event System.Action OnInventoryChanged;

    public IReadOnlyList<ItemSO> Items => _items;

    // ── 아이템 추가 ──────────────────────────────────────────
    public void AddItem(ItemSO item)
    {
        if (item == null) return;
        _items.Add(item);
        OnInventoryChanged?.Invoke();
    }

    // ── 누적 스탯 합산 ────────────────────────────────────────
    /// <summary>보유 중인 모든 아이템의 특정 스탯 합산값 반환</summary>
    public float GetTotal(StatType type)
    {
        float total = 0f;
        foreach (var item in _items)
            foreach (var mod in item.modifiers)
                if (mod.Type == type) total += mod.Value;
        return total;
    }
}
