using System;

/// <summary>
/// 상점 매대 카테고리.
/// 디자이너가 매대(ShopStallInteraction)에 인스펙터로 지정한다.
/// SHOP_PRICE_DATA 차트의 string("item"/"weapon")과 1:1 매핑.
/// </summary>
public enum ShopCategory
{
    Item   = 0,
    Weapon = 1,
}

/// <summary>
/// ShopCategory ↔ string("item"/"weapon") 변환 헬퍼.
/// SHOP_PRICE_DATA / ShopDataManager.GetPool 호출 경계에서 사용.
/// </summary>
public static class ShopCategoryExtensions
{
    public const string CategoryItemString   = "item";
    public const string CategoryWeaponString = "weapon";

    /// <summary>enum → 차트 문자열.</summary>
    public static string ToChartString(this ShopCategory cat)
    {
        switch (cat)
        {
            case ShopCategory.Item:   return CategoryItemString;
            case ShopCategory.Weapon: return CategoryWeaponString;
            default:                  return CategoryItemString;
        }
    }

    /// <summary>차트 문자열 → enum. 잘못된 값은 Item으로 폴백.</summary>
    public static ShopCategory FromChartString(string raw)
    {
        if (string.IsNullOrEmpty(raw)) return ShopCategory.Item;
        if (string.Equals(raw, CategoryWeaponString, StringComparison.OrdinalIgnoreCase))
            return ShopCategory.Weapon;
        return ShopCategory.Item;
    }
}
