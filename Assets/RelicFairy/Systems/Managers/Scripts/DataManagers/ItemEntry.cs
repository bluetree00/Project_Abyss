using System.Collections.Generic;

/// <summary>
/// 뒤끝 ITEM_DATA 차트 1행 = 아이템 효과 1슬롯.
/// item_id(또는 passive_id)로 그룹핑하여 다중 효과 아이템 표현.
///
/// ■ 기존 테이블: item_id, item_name, rarity, category, ... 전체 컬럼
/// ■ 새 테이블:   passive_id, slot, effect_type, trigger, value, max_stack, duration, description
///                (메타 정보는 별도 테이블에서 관리)
/// </summary>
[System.Serializable]
public class ItemEntry
{
    public string item_id;          // 기존 item_id 또는 새 passive_id
    public string passive_id;       // 새 CSV 호환 — item_id가 비면 이 값 사용
    public string item_name;
    public string rarity;           // Common / Rare / Epic / Legendary (구 CSV)
    public string grade;            // 신 CSV에서는 grade 컬럼 사용 (rarity와 호환)
    public string category;         // Ring, Necklace, Boots, Gloves, Belt, Charm, Active
    public int    slot;
    public string effect_type;      // MeleeAttack, MeleeDamage, Lifesteal, Heal, ...
    public string trigger;          // Always, OnHit, OnKill, WithFireWeapon, HPBelow50 ...
    public float  value;
    public float  value2;
    public float  value3;
    public int    max_stack;
    public float  duration;
    public float  cooldown;
    public int    shape_id;         // MERLIN_RUNE_PIECE_DATA 참조

    /// <summary>
    /// 룬 속성(ElementDef Id: FIRE/ICE/ELECTRIC/GRASS/LIGHT/DARK). <b>빈 값 = 범용</b>(어느 존에나 배치).
    /// <para>아래 등급은 범용, 윗 등급은 전용 — Common·Rare는 순수 스탯이라 비우고,
    /// Epic·Legendary는 효과 자체가 속성이므로 반드시 채운다. 안 채우면 「마그마 분출」이
    /// 얼음 존에만 놓이는 모순이 생긴다(<see cref="RuneZoneRule"/>).</para>
    /// </summary>
    public string element;
    public string icon_key;
    public string description;
    public int    stat_version;

    /// <summary>item_id 또는 passive_id 중 유효한 값 반환.</summary>
    public string ResolvedId => !string.IsNullOrEmpty(item_id) ? item_id : passive_id;

    /// <summary>rarity 또는 grade 중 유효한 값 반환 (대소문자 정규화).</summary>
    public string ResolvedRarity
    {
        get
        {
            var raw = !string.IsNullOrEmpty(rarity) ? rarity : grade;
            if (string.IsNullOrEmpty(raw)) return null;
            // 첫 글자 대문자로 정규화 (common → Common, legendary → Legendary)
            return char.ToUpperInvariant(raw[0]) + raw.Substring(1).ToLowerInvariant();
        }
    }
}

[System.Serializable]
public class ItemEntryCollection
{
    public List<ItemEntry> items;
}
