using UnityEngine;

/// <summary>
/// 아이템 등급별 표시 색상 (UI / 월드 텍스트 공용).
/// 코드 상수로 우선 정의. 추후 SO 분리 가능.
/// </summary>
public static class RarityColorTable
{
    public static Color Get(ItemRarity rarity)
    {
        switch (rarity)
        {
            case ItemRarity.Common:    return Color.white;
            case ItemRarity.Rare:      return new Color(0.4f, 0.7f, 1f);   // 청
            case ItemRarity.Epic:      return new Color(0.8f, 0.4f, 1f);   // 보라
            case ItemRarity.Legendary: return new Color(1f, 0.84f, 0.2f); // 금
            default:                   return Color.white;
        }
    }

    /// <summary>RichText 태그용 #RRGGBB(AA) 16진 문자열.</summary>
    public static string GetHex(ItemRarity rarity)
    {
        return ColorUtility.ToHtmlStringRGB(Get(rarity));
    }
}
