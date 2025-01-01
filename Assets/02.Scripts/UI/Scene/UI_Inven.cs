using System;
using System.Reflection;
using UnityEngine;

public class UI_Inven : UI_Scene
{
    enum GameObjects
    {
        GridPanel  // 아이템을 표시할 그리드 패널
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

        // GridPanel을 가져오기
        Bind<GameObject>(typeof(GameObjects));
        GameObject gridPanel = Get<GameObject>((int)GameObjects.GridPanel);

        // 기존 아이템 UI 삭제
        foreach (Transform child in gridPanel.transform)
        {
            Managers.Resource.Destroy(child.gameObject);
        }

        // 아이템 리스트 기반으로 UI 생성
        foreach (ItemData itemData in invenData.ItemList)
        {
            // 매개변수로 받은 UIType에 따라 동적으로 UI 생성
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
            // MakeSubItem<T> 메서드를 동적으로 호출
            MethodInfo method = typeof(UIManager).GetMethod("MakeSubItem").MakeGenericMethod(uiType);

            // Managers.UI를 명시적으로 참조
            object itemObject = method.Invoke(Managers.UI, new object[] { parent, null });

            // 생성된 UI에 데이터를 설정
            if (itemObject is UI_Inven_Item invenItem)
            {
                invenItem.SetInfo(name);
            }
            else
            {
                Debug.LogError($"UI 생성 실패: {uiType.Name}는 UI_Inven_Item 타입이 아닙니다.");
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"UI 생성 중 오류 발생: {ex.Message}");
        }
    }

}
