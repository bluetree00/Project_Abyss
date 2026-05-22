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
    public readonly int GainedEssence;   // 심연의 정수 (영구 성장 재화)
    public readonly ItemStack[] GainedItems;
    public readonly string Reason;

    public EndRunResult(bool isCleared, ChapterId chapter, int gainedGold, int gainedEssence, ItemStack[] gainedItems, string reason)
    {
        IsCleared     = isCleared;
        Chapter       = chapter;
        GainedGold    = gainedGold;
        GainedEssence = gainedEssence;
        GainedItems   = gainedItems ?? Array.Empty<ItemStack>();
        Reason        = reason;
    }
}
