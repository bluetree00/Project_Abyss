using System.Collections;
using System.Collections.Generic;
using Unity.VisualScripting;
using UnityEngine;

public class UIManager
{
    int _order = 10;

    Stack<UI_Popup> _popupStack = new Stack<UI_Popup>();
    UI_Scene _sceneUI = null;

    public GameObject Root
    {
        get
        {
			GameObject root = GameObject.Find("@UI_Root");
			if (root == null)
				root = new GameObject { name = "@UI_Root" };
            return root;
		}
    }

    public void SetCanvas(GameObject go, bool sort = true)
    {
        Canvas canvas = Util.GetOrAddComponent<Canvas>(go);
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.overrideSorting = true;

        if (sort)
        {
            canvas.sortingOrder = _order;
            _order++;
        }
        else
        {
            canvas.sortingOrder = 0;
        }
    }

   // 월드 스페이스용 UI를 가져온후 메인 카메라를 넣어줌
    public T MakeWorldSpaceUI<T>(Transform parent = null, string name = null) where T : UI_Base 
	{
		if (string.IsNullOrEmpty(name))
        {
            name = typeof(T).Name;
        }
	
		GameObject go = Managers.Resource.Instantiate($"UI/WorldSpace/{name}");
		if (parent != null)
        {
            go.transform.SetParent(parent);
        }

        Canvas canvas = go.GetOrAddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.worldCamera = Camera.main;
			
		return Util.GetOrAddComponent<T>(go);
	}


	public T MakeSubItem<T>(Transform parent = null, string name = null) where T : UI_Base
	{
		if (string.IsNullOrEmpty(name))
			name = typeof(T).Name;

		GameObject go = Managers.Resource.Instantiate($"UI/SubItem/{name}");
		if (parent != null)
			go.transform.SetParent(parent);

		return Util.GetOrAddComponent<T>(go);
	}

    public T MakeAugment<T>(Transform parent = null, string name = null) where T : UI_Base
	{
		if (string.IsNullOrEmpty(name))
			name = typeof(T).Name;

        GameObject go = Managers.Resource.Instantiate($"UI/Augments/{name}");
		if (parent != null)
			go.transform.SetParent(parent);

		return Util.GetOrAddComponent<T>(go);
	}

	public T ShowSceneUI<T>(string name = null) where T : UI_Scene
	{
		if (string.IsNullOrEmpty(name))
			name = typeof(T).Name;

		GameObject go = Managers.Resource.Instantiate($"UI/Scene/{name}");
		T sceneUI = Util.GetOrAddComponent<T>(go);
        _sceneUI = sceneUI;

		go.transform.SetParent(Root.transform);

		return sceneUI;
	}

	public T ShowPopupUI<T>(string name = null) where T : UI_Popup
    {
        if (string.IsNullOrEmpty(name))
            name = typeof(T).Name;

        GameObject go = Managers.Resource.Instantiate($"UI/Popup/{name}");
        T popup = Util.GetOrAddComponent<T>(go);
        _popupStack.Push(popup);

        go.transform.SetParent(Root.transform);

		return popup;
    }

    public UI_Augment_Choice ShowAugmentChoiceUI(List<AugmentData> availableAugments)
    {
        UI_Augment_Choice augmentChoiceUI = ShowPopupUI<UI_Augment_Choice>();
        
        // 증강 UI 초기화 및 선택 항목 표시
        augmentChoiceUI.InitAugments(availableAugments);
        augmentChoiceUI.ShowAugmentChoices(); // 선택 가능한 증강 UI 표시
        return augmentChoiceUI;
    }

    
    public void ClosePopupUI(UI_Popup popup)
    {
		if (_popupStack.Count == 0)
			return;

        if (_popupStack.Peek() != popup)
        {
            Debug.Log("Close Popup Failed!");
            return;
        }

        ClosePopupUI();
    }

    public void ClosePopupUI()
    {
        if (_popupStack.Count == 0)
            return;

        UI_Popup popup = _popupStack.Pop();
        Managers.Resource.Destroy(popup.gameObject);
        popup = null;
        _order--;
    }

    public void CloseAllPopupUI()
    {
        while (_popupStack.Count > 0)
            ClosePopupUI();
    }

    public void Clear()
    {
        CloseAllPopupUI();
        _sceneUI = null;
    }

    //인벤토리 아이템 추가 방식 uiType가 핵심 추가될 UI 형태 ex) UI_EquipmentItem 만약 다인 플레이시 자신의 이벤을 플레이어가 생성 저장필요
    public void InvenPushItem(string name, string uiType)
    {
        // ScriptableObject 로드
        InvenData invenData = Managers.Resource.Load<InvenData>("Data/ItemData/Inven/InvenData");
        if (invenData == null)
        {
            Debug.LogError("인벤토리 데이터를 로드하지 못했습니다!");
            return;
        }

        // 중복 아이템 방지
        if (invenData.ItemList.Exists(item => item.Name == name && item.UIType == uiType))
        {
            Debug.LogWarning($"이미 존재하는 아이템: {name}");
            return;
        }

        // 아이템 추가
        invenData.AddItem(name, uiType);
    }


}
