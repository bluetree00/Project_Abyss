using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 아이템 정의 ScriptableObject
/// - 아이템이 부여하는 스탯 수정자 목록을 보유
/// - 인스턴스 간 공유되므로 런타임에 수정 금지
/// </summary>
[CreateAssetMenu(fileName = "NewItem", menuName = "Item/ItemSO")]
public class ItemSO : ScriptableObject
{
    [Header("기본 정보")]
    public string itemId;           // 고유 식별자 (ex. "ring_atk_01")
    public string displayName;      // UI 표시 이름
    public Sprite icon;
    public string iconKey;          // Addressables 아이콘 키

    [Header("분류")]
    public ItemRarity rarity;
    public ItemCategory category;

    [Header("블록")]
    [Tooltip("BLOCK_SHAPE_DATA의 shape_id 참조. 0이면 블록 없음.")]
    public int shapeId;

    [Header("스탯 수정자")]
    public List<StatModifier> modifiers = new List<StatModifier>();
}

public enum ItemRarity
{
    Common,
    Rare,
    Epic,
    Legendary
}

public enum ItemCategory
{
    Ring,
    Necklace,
    Boots,
    Gloves,
    Belt,
    Charm,
    Active
}
