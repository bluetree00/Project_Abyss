using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 인게임 전용 플레이어 런 상태(예시)
/// - 실제 프로젝트에 맞게 HP/버프/스탯 등을 추가
/// </summary>
public sealed class PlayerRunState
{
    public int TempGold { get; private set; }

    public void AddTempGold(int amount) => TempGold += amount;
}

/// <summary>
/// 아이템 스택(예시)
/// </summary>
public readonly struct ItemStack
{
    public readonly ItemId ItemId;
    public readonly int Count;

    public ItemStack(ItemId itemId, int count)
    {
        ItemId = itemId;
        Count = count;
    }
}
