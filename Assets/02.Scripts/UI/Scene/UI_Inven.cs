using System.Collections.Generic;
using UnityEngine;

public class UI_Inven : UI_Scene
{
    enum GameObjects
    {
        GridPanel
    }

    public override void Init()
    {
        base.Init();

        // ResourceManager를 통해 ScriptableObject 로드
        InvenData invenData = Managers.Resource.Load<InvenData>("Data/ItemData/Inven"); // "Data/InvenData"는 Resources 폴더 경로
        if (invenData == null)
        {
            Debug.LogError("인벤토리 데이터를 로드하지 못했습니다!");
            return;
        }

        Bind<GameObject>(typeof(GameObjects));

        GameObject gridPanel = Get<GameObject>((int)GameObjects.GridPanel);
        foreach (Transform child in gridPanel.transform)
        {
            Managers.Resource.Destroy(child.gameObject);
        }

        // 로드한 InvenData로 UI 초기화
        foreach (ItemData itemData in invenData.ItemList)
        {
            GameObject item = Managers.UI.MakeSubItem<UI_Inven_Item>(gridPanel.transform).gameObject;
            UI_Inven_Item invenItem = item.GetOrAddComponent<UI_Inven_Item>();
            invenItem.SetInfo(itemData); // ItemData를 UI에 전달
        }
    }
}
