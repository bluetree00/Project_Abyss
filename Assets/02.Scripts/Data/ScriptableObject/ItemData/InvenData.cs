using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "InvenData", menuName = "Item/InvenData")]
public class InvenData : ScriptableObject
{
    [SerializeField]
    public List<ItemData> ItemList = new List<ItemData>(); // 동적 리스트 사용
}

[System.Serializable]
public class ItemData
{
    public string Name;     // 아이템 이름
    public Sprite Icon;     // 아이템 아이콘
    public int Quantity;    // 아이템 수량
}
