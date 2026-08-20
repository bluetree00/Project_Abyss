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

    // ── 속성(랜덤) ──
    /// <summary>룬 고유 속성(ElementDef Id: FIRE/ICE/ELECTRIC/GRASS/LIGHT/DARK). 생성 시 랜덤 배정.
    /// 이 속성의 존에 배치하면 해당 속성 시너지가 쌓인다(배치 유도용 — 판이 매칭 칸을 강조).
    /// 직렬화되므로 세이브 복원 시 유지된다(빈 경우에만 새로 배정).</summary>
    public string element;

    // ── 액티브 ──
    public float cooldown;

    // ── 효과 슬롯 ──
    public List<ItemEffectSlot> effects = new List<ItemEffectSlot>();

    // ── 속성 배정 ──

    /// <summary>
    /// 속성을 확정한다. 이미 있으면 유지(데이터가 지정했거나 세이브 복원).
    ///
    /// <para><b>모든 등급이 속성을 갖는다.</b> Common·Rare는 데이터에 속성이 없으므로
    /// 드랍 시점에 6속성 중 <b>무작위</b>로 배정한다 — 같은 「힘의 룬」이라도 이번 판에서는
    /// 불, 다음 판에서는 얼음으로 나온다. 이게 판마다 배치가 달라지는 이유가 된다.</para>
    ///
    /// <para>Epic·Legendary는 효과 자체가 속성이라 데이터(<c>ITEM_DATA.element</c>)가 고정한다 —
    /// 「마그마 분출」이 얼음 존에 놓이면 모순이다. 그래서 이쪽만 미지정을 경고로 드러낸다.</para>
    ///
    /// <para>한때 Common·Rare를 빈 값(=범용, 어디든 배치)으로 뒀는데, 그러면 120종 중 84종이
    /// <see cref="RuneZoneRule"/>의 제약을 받지 않아 속성 존이 색깔만 다른 칸이 됐고,
    /// 속성 블록 타일도 상위 36종에서만 보였다.</para>
    ///
    /// <para>배정은 <b>인스턴스 단위</b>다. 직렬화되므로 세이브 복원 시 그대로 유지된다.</para>
    /// </summary>
    public void EnsureElement()
    {
        if (!string.IsNullOrEmpty(element)) return;

        var order = ElementDef.Order;
        element = order != null && order.Count > 0
            ? order[UnityEngine.Random.Range(0, order.Count)]
            : ElementDef.CenterId;

        // 상위 등급은 데이터가 속성을 고정해야 한다 — 비어 있으면 데이터 결함이므로 드러낸다.
        // 하위 등급의 무작위 배정은 정상 동작이라 조용히 지나간다.
        if (rarity == ItemRarity.Epic || rarity == ItemRarity.Legendary)
            UnityEngine.Debug.LogWarning(
                $"[RuntimeItemData] '{itemId}'({rarity}) 속성 미지정 — ITEM_DATA.element를 채워야 한다. 임시 배정: {element}");
    }

    // ── 세이브 복원 ──

    /// <summary>
    /// JsonUtility 왕복에서 잃어버린 <b>에셋 참조</b>를 SO에서 다시 채운다. 이어하기 복원 직후 1회 호출.
    ///
    /// icon은 Sprite 직참조라 JSON에는 인스턴스 ID로만 남고, 다음 실행에서는 null로 되살아난다.
    /// iconKey로 재로드하면 될 것 같지만 ItemSO의 iconKey는 전 항목이 비어 있어 메울 수 없다 —
    /// itemId로 SO를 되찾는 것이 유일한 경로다.
    ///
    /// 값 데이터(효과·속성·등급)는 세이브가 정본이므로 건드리지 않는다.
    /// </summary>
    public void RestoreAssetRefs()
    {
        if (icon != null) return;

        var so = ItemSORegistry.Find(itemId);
        if (so != null && so.icon != null) icon = so.icon;
    }

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

        data.EnsureElement();
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

        // 속성 — CSV가 정본. 빈 값이면 범용(어느 존에나)이고, 그건 Common·Rare의 정상 상태다.
        foreach (var e in entries)
        {
            if (string.IsNullOrEmpty(e?.element)) continue;
            data.element = e.element;
            break;
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

        data.EnsureElement();
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
