using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 런타임 아이템 데이터.
/// ItemSO 또는 서버 ItemEntry에서 생성.
/// </summary>
[System.Serializable]
public class RuntimeItemData
{
    // ── 인스턴스 식별 ──
    public string instanceId = System.Guid.NewGuid().ToString();

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

    /// <summary>
    /// SO에서 생성.
    /// 효과의 정본은 CSV(ITEM_DATA)이므로, 해당 itemId의 CSV 엔트리가 있으면
    /// FromServer에 위임한다(FromServer가 ItemSORegistry로 SO 표시정보까지 병합).
    /// CSV에 없는 SO만 아래 modifiers 기반 폴백을 탄다.
    /// </summary>
    public static RuntimeItemData FromSO(ItemSO so)
    {
        if (so == null) return null;

        if (!string.IsNullOrEmpty(so.itemId))
        {
            var csvEntries = Managers.ItemData?.GetItem(so.itemId);
            if (csvEntries != null && csvEntries.Count > 0)
            {
                var fromCsv = FromServer(csvEntries);
                if (fromCsv != null) return fromCsv;
            }
        }

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
                vfxKey     = so.effectVfxKey,
            });
        }

        // shapeId가 SO에 미설정이면 서버 데이터에서 보완 (ItemSO 인스펙터 할당 누락 대응)
        if (data.shapeId == 0 && !string.IsNullOrEmpty(data.itemId))
        {
            var entries = Managers.ItemData?.GetItem(data.itemId);
            if (entries != null)
                foreach (var e in entries)
                    if (e.shape_id > 0) { data.shapeId = e.shape_id; break; }
        }

        // 최후 폴백: ITEM_DATA에도 없으면 itemId 해시 기반으로 shape 1~10 순환 할당
        if (data.shapeId == 0 && !string.IsNullOrEmpty(data.itemId))
        {
            data.shapeId = (UnityEngine.Mathf.Abs(data.itemId.GetHashCode()) % 10) + 1;
            Debug.LogWarning($"[RuntimeItemData] {data.itemId}: shapeId 미설정 — 임시 할당 shapeId={data.shapeId}");
        }

        return data;
    }

    /// <summary>서버 ItemEntry 목록에서 생성. SO가 있으면 표시 정보 병합.</summary>
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

        // CSV rarity/grade 파싱 (값이 있으면 적용)
        var resolvedRarity = meta.ResolvedRarity;
        bool hasCSVRarity = !string.IsNullOrEmpty(resolvedRarity);
        if (hasCSVRarity)
        {
            data.rarity = resolvedRarity switch
            {
                "Rare"      => ItemRarity.Rare,
                "Epic"      => ItemRarity.Epic,
                "Legendary" => ItemRarity.Legendary,
                _           => ItemRarity.Common,
            };
        }

        // CSV category 파싱 (값이 있으면 적용)
        bool hasCSVCategory = !string.IsNullOrEmpty(meta.category);
        if (hasCSVCategory)
        {
            data.category = meta.category switch
            {
                "Necklace" => ItemCategory.Necklace,
                "Boots"    => ItemCategory.Boots,
                "Gloves"   => ItemCategory.Gloves,
                "Belt"     => ItemCategory.Belt,
                "Charm"    => ItemCategory.Charm,
                "Active"   => ItemCategory.Active,
                _          => ItemCategory.Ring,
            };
        }

        // SO 병합 — CSV에 없는 표시 정보를 SO에서 채움
        var so = ItemSORegistry.Find(meta.ResolvedId);
        if (so != null)
        {
            if (so.icon != null) data.icon = so.icon;
            if (!string.IsNullOrEmpty(so.iconKey) && string.IsNullOrEmpty(data.iconKey))
                data.iconKey = so.iconKey;
            if (string.IsNullOrEmpty(data.displayName))
                data.displayName = so.displayName;
            if (!hasCSVRarity)
                data.rarity = so.rarity;
            if (!hasCSVCategory)
                data.category = so.category;
            if (data.shapeId == 0 && so.shapeId > 0)
                data.shapeId = so.shapeId;
        }

        // 효과 슬롯 — ItemSO의 VFX 키를 각 슬롯에 주입 (SO override)
        string soVfxKey = so?.effectVfxKey;
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
                description = e.description,
                vfxKey     = soVfxKey,
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

    /// <summary>
    /// CSV(ITEM_DATA) 원문 설명. 표시 레이어가 있으면 우선 사용(없으면 포맷터 조립 폴백).
    /// 동작/밸런스에는 영향 없음 — 순수 표시용.
    /// </summary>
    public string description;

    /// <summary>
    /// 이 슬롯의 VFX Addressable 키. 빈 값이면 Effect 클래스의 기본 키 사용.
    /// ItemSO.effectVfxKey에서 RuntimeItemData 생성 시 복사됨.
    /// </summary>
    public string vfxKey;
}
