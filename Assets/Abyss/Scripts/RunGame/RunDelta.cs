using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 런 중 누적된 "영구 반영 후보" (delta)
/// </summary>
public sealed class RunDelta
{
    public int GainedGold;
    public readonly List<ItemStack> GainedItems = new List<ItemStack>();
}