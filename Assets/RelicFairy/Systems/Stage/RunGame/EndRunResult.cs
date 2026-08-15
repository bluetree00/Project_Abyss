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

    // ── 기억의 제단 기록용 ────────────────────────────────
    // 해금 <b>할인 조건</b>과 업적 <b>진척</b>이 같은 값을 본다(정본 §2·§6).
    public readonly int AbyssDepth;
    public readonly int RoomClears;
    public readonly int Kills;
    public readonly bool PotionUsed;
    public readonly int  SpecialVisits;
    public readonly int  FlawlessChapters;
    public readonly int EliteKills;
    public readonly int BossKills;
    public readonly int ShopUses;
    public readonly int RefineUses;
    public readonly int MaxEnhance;

    public EndRunResult(bool isCleared, ChapterId chapter, int gainedGold, int gainedEssence, ItemStack[] gainedItems, string reason,
                        int abyssDepth = 0, int roomClears = 0, int kills = 0, int eliteKills = 0, int bossKills = 0,
                        int shopUses = 0, int refineUses = 0, int maxEnhance = 0,
                        bool potionUsed = false, int specialVisits = 0, int flawless = 0)
    {
        IsCleared     = isCleared;
        Chapter       = chapter;
        GainedGold    = gainedGold;
        GainedEssence = gainedEssence;
        GainedItems   = gainedItems ?? Array.Empty<ItemStack>();
        Reason        = reason;

        AbyssDepth    = abyssDepth;
        RoomClears    = roomClears;
        Kills         = kills;
        PotionUsed       = potionUsed;
        SpecialVisits    = specialVisits;
        FlawlessChapters = flawless;
        EliteKills    = eliteKills;
        BossKills     = bossKills;
        ShopUses      = shopUses;
        RefineUses    = refineUses;
        MaxEnhance    = maxEnhance;
    }
}
