using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "InvenData", menuName = "Item/InvenData")]
public class InvenData : ScriptableObject
{
    [SerializeField]
    public List<ItemData> ItemList = new List<ItemData>(); // 동적 리스트 사용

     // 새로운 아이템 추가 메서드
    public void AddItem(string name, string uiType)
    {
        ItemData newItem = new ItemData { Name = name, UIType = uiType };
        ItemList.Add(newItem);
    }
}

[System.Serializable]
public class ItemData
{
    public string Name;     // 아이템 이름
    public string  UIType;

}
