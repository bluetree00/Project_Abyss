using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 런 중 획득한 아이템을 추적.
///
/// ■ 아이템은 그리드에 배치되어야만 효과가 적용된다.
/// ■ PlacedItems  — 그리드에 배치됨 (효과 적용)
/// ■ StagingItems — 미배치 / 재배치 중 임시 보관 (효과 없음)
/// </summary>
public sealed class RunItemInventory
{
    /// <summary>보관함 칸 배열 상한(제단 「룬 보관함 +2」까지). 실제 칸 수는 <see cref="StagingCapacity"/>.</summary>
    public const int MaxStagingCapacity = 7;
    /// <summary>보관함 바가 그릴 수 있는 칸 상한 — 상한을 넘친 룬(획득 · 판에서 끌어냄)까지 보이게 넉넉히. 넘친 룬은 정리해야 판이 닫힌다.</summary>
    public const int MaxStagingView = 16;

    /// <summary>이번 런 보관함 칸 수 — 기본 5, 기억의 제단 「룬 보관함 +1 · +2」로 6 · 7(09-29).</summary>
    public static int StagingCapacity => Mathf.Min(MaxStagingCapacity, MemoryAltarService.RuneStorageCapacity);

    private readonly List<RuntimeItemData> _placedItems  = new();
    private readonly List<RuntimeItemData> _stagingItems = new();

    /// <summary>그리드에 배치된 아이템. 효과 적용 기준.</summary>
    public IReadOnlyList<RuntimeItemData> PlacedItems  => _placedItems;

    /// <summary>보관함(임시). 재배치 중이거나 아직 배치되지 않은 아이템.</summary>
    public IReadOnlyList<RuntimeItemData> StagingItems => _stagingItems;

    public int  PlacedCount   => _placedItems.Count;
    public int  StagingCount  => _stagingItems.Count;
    public bool IsStagingFull => _stagingItems.Count >= StagingCapacity;
    /// <summary>상한을 넘었다 — 룬판이 닫히기 전에 놓거나 분해해야 한다.</summary>
    public bool IsOverCapacity => _stagingItems.Count > StagingCapacity;
    public int  OverflowCount  => Mathf.Max(0, _stagingItems.Count - StagingCapacity);

    /// <summary>그리드 배치 기준 변경 시 발생 — ItemEffectManager.Rebuild 트리거.</summary>
    public event System.Action OnPlacedChanged;

    /// <summary>보관함 변경 시 발생 — StagingAreaView 갱신 트리거.</summary>
    public event System.Action OnStagingChanged;

    // ── 쿼리 ─────────────────────────────────────────────────

    public bool IsPlaced(string instanceId)
    {
        foreach (var item in _placedItems)
            if (item.instanceId == instanceId) return true;
        return false;
    }

    public bool IsStaging(string instanceId)
    {
        foreach (var item in _stagingItems)
            if (item.instanceId == instanceId) return true;
        return false;
    }

    public bool HasItem(string itemId)
    {
        foreach (var item in _placedItems)
            if (item.itemId == itemId) return true;
        foreach (var item in _stagingItems)
            if (item.itemId == itemId) return true;
        return false;
    }

    public int CountItem(string itemId)
    {
        int count = 0;
        foreach (var item in _placedItems)
            if (item.itemId == itemId) count++;
        foreach (var item in _stagingItems)
            if (item.itemId == itemId) count++;
        return count;
    }

    // ── 아이템 획득 ──────────────────────────────────────────

    /// <summary>
    /// 아이템 획득 시 보관함에 추가.
    /// 효과는 그리드 배치(PlaceItem) 후 적용됨.
    /// maxStack 체크는 배치(PlaceItem) 시에만 수행 — 보관함은 임시 보관 장소이므로 중복 허용.
    /// </summary>
    public bool AddToStaging(RuntimeItemData item)
    {
        if (item == null) return false;
        if (_stagingItems.Count >= StagingCapacity)
        {
            Debug.LogWarning($"[RunItemInventory] 보관함 가득참 ({StagingCapacity}개) — 추가 불가: {item.itemId}");
            return false;
        }

        _stagingItems.Add(item);
        QuestEvents.ReportItemCollect(item.itemId ?? "Unknown");
        Managers.Sound?.PlayEvent(SoundEvent.ItemPickup);
        OnStagingChanged?.Invoke();
        return true;
    }

    /// <summary>
    /// 받은 룬(룬 보상 · 행상 구매)을 상한을 넘겨서라도 넣는다 — 넘친 만큼은 룬판에서 놓거나 분해해야 닫힌다.
    /// 예전엔 가득이면 「보류」 한 칸에 들고 다녀, 연달아 받으면 앞의 것을 덮어쓸 수 있었고 받은 건지도 애매했다(10-01).
    /// 상점 · 정제소처럼 <b>값을 치르기 전에 막을 수 있는 곳</b>은 그대로 <see cref="AddToStaging"/>(가득이면 거절)을 쓴다.
    /// </summary>
    public bool AddToStagingOverflow(RuntimeItemData item)
    {
        if (item == null) return false;
        _stagingItems.Add(item);
        QuestEvents.ReportItemCollect(item.itemId ?? "Unknown");
        Managers.Sound?.PlayEvent(SoundEvent.ItemPickup);
        OnStagingChanged?.Invoke();
        return true;
    }

    // ── 배치 / 제거 ──────────────────────────────────────────

    /// <summary>보관함 → 그리드 배치. 효과 즉시 적용.</summary>
    public void PlaceItem(RuntimeItemData item)
    {
        if (item == null) return;

        if (_stagingItems.Remove(item))
        {
            _placedItems.Add(item);
            OnPlacedChanged?.Invoke();
            OnStagingChanged?.Invoke();
        }
    }

    /// <summary>그리드 → 보관함 복귀. 효과 즉시 해제.</summary>
    public void UnplaceItem(RuntimeItemData item)
    {
        if (item == null) return;

        if (_placedItems.Remove(item))
        {
            _stagingItems.Add(item);
            OnPlacedChanged?.Invoke();
            OnStagingChanged?.Invoke();
        }
    }

    /// <summary>보관함에서 아이템 폐기.</summary>
    public void DiscardFromStaging(RuntimeItemData item)
    {
        if (item == null) return;
        if (_stagingItems.Remove(item))
            OnStagingChanged?.Invoke();
    }

    /// <summary>배치된 아이템 직접 제거 (런 종료 등 내부 정리용).</summary>
    public void RemovePlaced(RuntimeItemData item)
    {
        if (item == null) return;
        if (_placedItems.Remove(item))
            OnPlacedChanged?.Invoke();
    }

    public void Clear()
    {
        _placedItems.Clear();
        _stagingItems.Clear();
        OnPlacedChanged?.Invoke();
        OnStagingChanged?.Invoke();
    }

    /// <summary>이어하기 복원용. 부작용 없이 배치 상태로 일괄 복원.</summary>
    public void RestorePlacedItems(IEnumerable<RuntimeItemData> items)
    {
        if (items == null) return;
        foreach (var item in items)
        {
            if (item == null) continue;
            item.RestoreAssetRefs();   // 아이콘(Sprite 직참조)은 JSON 왕복에서 사라진다 — SO에서 되찾는다
            _placedItems.Add(item);
        }
        if (_placedItems.Count > 0)
            OnPlacedChanged?.Invoke();
    }

    /// <summary>이어하기 복원용. 부작용 없이 보관함 상태로 일괄 복원.</summary>
    public void RestoreStagingItems(IEnumerable<RuntimeItemData> items)
    {
        if (items == null) return;
        foreach (var item in items)
        {
            if (item == null) continue;
            item.RestoreAssetRefs();   // 아이콘(Sprite 직참조)은 JSON 왕복에서 사라진다 — SO에서 되찾는다
            _stagingItems.Add(item);
        }
        if (_stagingItems.Count > 0)
            OnStagingChanged?.Invoke();
    }

    // ── 내부 ─────────────────────────────────────────────────

    private static int ResolveMaxStack(RuntimeItemData item)
    {
        if (item == null || string.IsNullOrEmpty(item.itemId)) return 1;
        var so = ItemSORegistry.Find(item.itemId);
        return so != null ? so.MaxStack : 1;
    }
}
