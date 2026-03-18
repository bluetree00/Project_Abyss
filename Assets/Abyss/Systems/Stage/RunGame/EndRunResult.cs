using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 런 종료 후 영구 반영에 사용할 결과 포맷
/// </summary>
public readonly struct EndRunResult
{
    public readonly bool IsCleared;
    public readonly ChapterId Chapter;
    public readonly int GainedGold;
    public readonly ItemStack[] GainedItems;
    public readonly string Reason;

    public EndRunResult(bool isCleared, ChapterId chapter, int gainedGold, ItemStack[] gainedItems, string reason)
    {
        IsCleared = isCleared;
        Chapter = chapter;
        GainedGold = gainedGold;
        GainedItems = gainedItems ?? Array.Empty<ItemStack>();
        Reason = reason;
    }
}
