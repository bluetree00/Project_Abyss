using UnityEngine;

/// <summary>
/// 상점 등급별 기본가 테이블.
/// price_override가 0인 ShopEntry에 대해 카테고리(item/weapon) + 등급 조합으로 기본가를 결정한다.
/// 인스턴스 간 공유되므로 런타임에 수정 금지.
/// </summary>
[CreateAssetMenu(fileName = "ShopPriceTable", menuName = "Abyss/Shop/Price Table")]
public class ShopPriceTableSO : ScriptableObject
{
    private const string CategoryItem   = "item";
    private const string CategoryWeapon = "weapon";

    [Header("아이템 등급별 기본가")]
    [SerializeField] private int itemCommon    = 50;
    [SerializeField] private int itemRare      = 150;
    [SerializeField] private int itemEpic      = 400;
    [SerializeField] private int itemLegendary = 1000;

    [Header("장비 등급별 기본가")]
    [SerializeField] private int weaponCommon    = 100;
    [SerializeField] private int weaponRare      = 300;
    [SerializeField] private int weaponEpic      = 700;
    [SerializeField] private int weaponLegendary = 1500;

    /// <summary>
    /// 카테고리 + 등급 조합으로 기본가 반환.
    /// category는 "item" / "weapon" (대소문자 무시).
    /// 잘못된 category는 Common 가격 폴백 + warning log.
    /// 음수 값은 0으로 보정.
    /// </summary>
    public int GetBasePrice(string category, ItemRarity rarity)
    {
        if (string.IsNullOrEmpty(category))
        {
            Debug.LogWarning("[ShopPriceTableSO] category is null/empty — fallback to itemCommon.");
            return Mathf.Max(0, itemCommon);
        }

        string normalized = category.ToLowerInvariant();
        if (normalized == CategoryWeapon)
            return Mathf.Max(0, GetWeaponPrice(rarity));
        if (normalized == CategoryItem)
            return Mathf.Max(0, GetItemPrice(rarity));

        Debug.LogWarning($"[ShopPriceTableSO] Unknown category '{category}' — fallback to itemCommon.");
        return Mathf.Max(0, itemCommon);
    }

    private int GetItemPrice(ItemRarity rarity)
    {
        switch (rarity)
        {
            case ItemRarity.Common:    return itemCommon;
            case ItemRarity.Rare:      return itemRare;
            case ItemRarity.Epic:      return itemEpic;
            case ItemRarity.Legendary: return itemLegendary;
            default:                   return itemCommon;
        }
    }

    private int GetWeaponPrice(ItemRarity rarity)
    {
        switch (rarity)
        {
            case ItemRarity.Common:    return weaponCommon;
            case ItemRarity.Rare:      return weaponRare;
            case ItemRarity.Epic:      return weaponEpic;
            case ItemRarity.Legendary: return weaponLegendary;
            default:                   return weaponCommon;
        }
    }
}
