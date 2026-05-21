using UnityEngine;

/// <summary>
/// 상점 진열대에 놓일 상품 1건.
/// 기존 ItemSO를 참조해 아이템 메타를 재사용하고, 가격과 가중치만 추가.
/// </summary>
[CreateAssetMenu(menuName = "Shop/ShopItem", fileName = "ShopItem_")]
public class ShopItemSO : ScriptableObject
{
    [Header("상품")]
    [SerializeField] private ItemSO item;

    [Header("가격 / 가중치")]
    [SerializeField, Min(0)] private int price;
    [SerializeField, Min(1)] private int weight = 1;

    public ItemSO Item => item;
    public int Price => price;
    public int Weight => weight;
}
