using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Unity.VisualScripting;
using UnityEngine;
using MapGeneratorManager;
using Cysharp.Threading.Tasks;

public class UIManager
{
    int _order = 10;

    Stack<UI_Popup> _popupStack = new Stack<UI_Popup>();
    UI_Scene _sceneUI = null;

    // 특정 UI를 추적하기 위한 Dictionary
    Dictionary<string, GameObject> _uiObjects = new Dictionary<string, GameObject>();
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

    public void ShowSceneUI<T>(string name = null) where T : UI_Scene
    {
        if (string.IsNullOrEmpty(name))
            name = typeof(T).Name;

        // 1️⃣ 이미 존재하면 재사용
        if (_uiObjects.TryGetValue(name, out GameObject existing))
        {
            existing.SetActive(true);
            _sceneUI = existing.GetComponent<T>();
            return;
        }

        // 2️⃣ Addressables 로드
        string addressKey = $"UI/Scene/{name}";
        Managers.AddressableManager.LoadAsset<GameObject>(addressKey, prefab =>
        {
            GameObject go = GameObject.Instantiate(prefab, Root.transform);
            go.name = name;

            SetCanvas(go, true);

            T sceneUI = Util.GetOrAddComponent<T>(go);
            _sceneUI = sceneUI;

            // 3️⃣ Dictionary에 추적 등록
            _uiObjects[name] = go;
        },
        () =>
        {
            Debug.LogError($"Scene UI Load Failed : {name}");
        });
    }


    public void ShowPopupUI<T>(string name = null) where T : UI_Popup
    {
        if (string.IsNullOrEmpty(name))
            name = typeof(T).Name;

        // 이미 열려 있으면 중복 방지
        if (_uiObjects.TryGetValue(name, out GameObject existing))
        {
            existing.SetActive(true);
            _popupStack.Push(existing.GetComponent<T>());
            return;
        }

        string addressKey = $"UI/Popup/{name}";
        Managers.AddressableManager.LoadAsset<GameObject>(addressKey, prefab =>
        {
            GameObject go = GameObject.Instantiate(prefab, Root.transform);
            go.name = name;

            SetCanvas(go, true);

            T popup = Util.GetOrAddComponent<T>(go);
            _popupStack.Push(popup);

            _uiObjects[name] = go;
        });
    }


    //TODO:다시 수정할 필요 있음
    // 팝업 UI를 표시하고 스택에 추가
    public bool HasPopup<T>() where T : UI_Popup
    {
        return _popupStack.Any(p => p is T);
    }


    // 특정 UI 제거
    public void CloseUI(string name)
    {
        if (!_uiObjects.TryGetValue(name, out GameObject ui))
            return;

        ui.SetActive(false);
        _order--;
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

    public void ClearAllUI()
    {
        foreach (var ui in _uiObjects.Values)
            GameObject.Destroy(ui);

        _uiObjects.Clear();
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

        // // 중복 아이템 방지 필요시 해제
        // if (invenData.ItemList.Exists(item => item.Name == name && item.UIType == uiType))
        // {
        //     Debug.LogWarning($"이미 존재하는 아이템: {name}");
        //     return;
        // }

        // UI가 이미 파괴되었는지 확인
        if (_uiObjects.ContainsKey(name) && _uiObjects[name] == null)
        {
            Debug.LogWarning($"UI [{name}]가 이미 파괴되었습니다. 아이템을 추가할 수 없습니다.");
            _uiObjects.Remove(name); // 파괴된 UI 객체 참조 제거
        }

        // 아이템 추가
        invenData.AddItem(name, uiType);

        // 인벤토리 UI 갱신
        if (_sceneUI is UI_Inven uiInven && _uiObjects.ContainsKey("UI_Inven"))
        {
            uiInven.RefreshInventory(); // UI 갱신
        }
        else
        {
            Debug.Log("닫혀있음");
        }
    }



}
