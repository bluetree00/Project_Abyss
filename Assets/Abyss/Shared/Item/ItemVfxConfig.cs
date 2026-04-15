using UnityEngine;

/// <summary>
/// 아이템 등급별 VFX Addressable 키 매핑.
/// </summary>
[CreateAssetMenu(menuName = "Abyss/Item/ItemVfxConfig")]
public class ItemVfxConfig : ScriptableObject
{
    [Header("등급별 디스플레이 VFX (Addressable Key)")]
    [SerializeField] private string commonVfxKey = "VFX_Item_Common";
    [SerializeField] private string rareVfxKey   = "VFX_Item_Rare";
    [SerializeField] private string epicVfxKey   = "VFX_Item_Epic";

    [Header("등급별 트레일 VFX (Addressable Key, 선택)")]
    [SerializeField] private string commonTrailKey = "VFX_Item_Trail_Common";
    [SerializeField] private string rareTrailKey   = "VFX_Item_Trail_Rare";
    [SerializeField] private string epicTrailKey   = "VFX_Item_Trail_Epic";

    public string GetDisplayVfxKey(ItemRarity rarity) => rarity switch
    {
        ItemRarity.Common => commonVfxKey,
        ItemRarity.Rare   => rareVfxKey,
        ItemRarity.Epic   => epicVfxKey,
        _                 => commonVfxKey,
    };

    public string GetTrailVfxKey(ItemRarity rarity) => rarity switch
    {
        ItemRarity.Common => commonTrailKey,
        ItemRarity.Rare   => rareTrailKey,
        ItemRarity.Epic   => epicTrailKey,
        _                 => commonTrailKey,
    };
}
