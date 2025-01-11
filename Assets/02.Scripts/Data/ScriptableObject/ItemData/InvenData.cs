using System;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "InvenData", menuName = "Item/InvenData")]
public class InvenData : ScriptableObject
{
    [SerializeField] public List<ItemData> ItemList = new List<ItemData>();

    // 델리게이트와 이벤트 선언
    public delegate void InventoryChangedDelegate();
    public event InventoryChangedDelegate OnInventoryChanged;

    // 아이템 추가 메서드
    public void AddItem(string name, string uiType)
    {
        ItemData newItem = new ItemData { Name = name, UIType = uiType };
        ItemList.Add(newItem);

        // 이벤트 호출 (구독된 메서드 실행)
        //OnInventoryChanged?.Invoke();

        
    }
}

[System.Serializable]
public class ItemData
{
    public string Name;     // 아이템 이름
    public string UIType;   // 아이템 UI 타입
}
