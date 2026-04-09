using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 런타임 아이템 데이터.
/// ItemSO 또는 서버 ItemEntry에서 생성.
/// </summary>
[System.Serializable]
public class RuntimeItemData
{
    // ── 기본 정보 ──
    public string itemId;
    public string displayName;
    public string iconKey;
    public Sprite icon;           // SO에서 생성 시 직접 참조, 서버 시 null
    public ItemRarity rarity;
    public ItemCategory category;

    // ── 블록 ──
    public int shapeId;

    // ── 액티브 ──
    public float cooldown;

    // ── 효과 슬롯 ──
    public List<ItemEffectSlot> effects = new List<ItemEffectSlot>();

    // ── 팩토리 ──

    /// <summary>SO에서 생성 (에디터/테스트용).</summary>
    public static RuntimeItemData FromSO(ItemSO so)
    {
        if (so == null) return null;
        var data = new RuntimeItemData
        {
            itemId      = so.itemId,
            displayName = so.displayName,
            iconKey     = so.iconKey,
            icon        = so.icon,
            rarity      = so.rarity,
            category    = so.category,
            shapeId     = so.shapeId,
        };

        foreach (var mod in so.modifiers)
        {
            data.effects.Add(new ItemEffectSlot
            {
                effectType = mod.Type.ToString(),
                trigger    = "Always",
                value      = mod.Value,
            });
        }

        return data;
    }

    /// <summary>서버 ItemEntry 목록에서 생성.</summary>
    public static RuntimeItemData FromServer(List<ItemEntry> entries)
    {
        if (entries == null || entries.Count == 0) return null;

        ItemEntry meta = null;
        foreach (var e in entries)
            if (e.slot == 1) { meta = e; break; }
        if (meta == null) meta = entries[0];

        var data = new RuntimeItemData
        {
            itemId      = meta.item_id,
            displayName = meta.item_name,
            iconKey     = meta.icon_key,
            shapeId     = meta.shape_id,
            cooldown    = meta.cooldown,
        };

        // rarity 파싱
        switch (meta.rarity)
        {
            case "Common": data.rarity = ItemRarity.Common; break;
            case "Rare":   data.rarity = ItemRarity.Rare;   break;
            case "Epic":   data.rarity = ItemRarity.Epic;    break;
            default:       data.rarity = ItemRarity.Common;  break;
        }

        // category 파싱
        switch (meta.category)
        {
            case "Ring":     data.category = ItemCategory.Ring;     break;
            case "Necklace": data.category = ItemCategory.Necklace; break;
            case "Boots":    data.category = ItemCategory.Boots;    break;
            case "Gloves":   data.category = ItemCategory.Gloves;   break;
            case "Belt":     data.category = ItemCategory.Belt;     break;
            case "Charm":    data.category = ItemCategory.Charm;    break;
            case "Active":   data.category = ItemCategory.Active;   break;
            default:         data.category = ItemCategory.Ring;      break;
        }

        // 효과 슬롯
        foreach (var e in entries)
        {
            if (string.IsNullOrEmpty(e.effect_type)) continue;
            data.effects.Add(new ItemEffectSlot
            {
                slot       = e.slot,
                effectType = e.effect_type,
                trigger    = e.trigger,
                value      = e.value,
                value2     = e.value2,
                value3     = e.value3,
                maxStack   = e.max_stack,
                duration   = e.duration,
            });
        }

        return data;
    }

    /// <summary>액티브 아이템인지 확인.</summary>
    public bool IsActive => category == ItemCategory.Active;
}

/// <summary>
/// 아이템 효과 슬롯 1개.
/// </summary>
[System.Serializable]
public class ItemEffectSlot
{
    public int    slot;
    public string effectType;   // MeleeAttack, Lifesteal, Heal, DamageAoE ...
    public string trigger;      // Always, OnHit, OnKill, OnUse, OnLowHp ...
    public float  value;
    public float  value2;
    public float  value3;
    public int    maxStack;
    public float  duration;
}
