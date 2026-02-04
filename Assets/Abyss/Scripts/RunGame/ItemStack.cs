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
