using System;
using UnityEngine;

public class UI_Inven : UI_Scene
{
    enum GameObjects
    {
        GridPanel // 아이템을 표시할 그리드 패널
    }

    public override void Init()
    {
        base.Init();

        // ScriptableObject 데이터 로드
        InvenData invenData = Managers.Resource.Load<InvenData>("Data/ItemData/Inven/InvenData");
        if (invenData == null)
        {
            Debug.LogError("인벤토리 데이터를 로드하지 못했습니다!");
            return;
        }

        // 이벤트 구독
        invenData.OnInventoryChanged += RefreshInventory;

        // 초기 UI 생성
        RefreshInventory();
    }

    // 인벤토리 UI를 새로 갱신하는 메서드
    public void RefreshInventory()
    {
        // ScriptableObject 데이터 로드
        InvenData invenData = Managers.Resource.Load<InvenData>("Data/ItemData/Inven/InvenData");
        if (invenData == null)
        {
            Debug.LogError("인벤토리 데이터를 로드하지 못했습니다!");
            return;
        }

        // GridPanel 가져오기
        if (_objects == null || !_objects.ContainsKey(typeof(GameObjects))) // 중복 방지
            Bind<GameObject>(typeof(GameObjects));
        
        GameObject gridPanel = Get<GameObject>((int)GameObjects.GridPanel);

        // 기존 아이템 UI 삭제
        foreach (Transform child in gridPanel.transform)
        {
            Managers.Resource.Destroy(child.gameObject);
        }

        // 현재 인벤토리 데이터 기반으로 UI 다시 생성
        foreach (ItemData itemData in invenData.ItemList)
        {
            Type itemType = Type.GetType(itemData.UIType);
            if (itemType == null)
            {
                Debug.LogError($"잘못된 UI 타입: {itemData.UIType}");
                continue;
            }

            CreateItemUI(itemType, gridPanel.transform, itemData.Name);
        }
    }


    private void CreateItemUI(Type uiType, Transform parent, string name)
    {
        try
        {
            // 동적으로 MakeSubItem<T> 메서드 호출
            var method = typeof(UIManager).GetMethod("MakeSubItem").MakeGenericMethod(uiType);
            var itemObject = method.Invoke(Managers.UI, new object[] { parent, null });

            if (itemObject is UI_Inven_Item invenItem)
            {
                invenItem.SetInfo(name);
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"UI 생성 중 오류 발생: {ex.Message}");
        }
    }
}
