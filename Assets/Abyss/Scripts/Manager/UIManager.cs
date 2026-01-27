using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Cysharp.Threading.Tasks;

public class UIManager
{
    int _order = 10;

    Stack<UI_Popup> _popupStack = new Stack<UI_Popup>();
    UI_Scene _sceneUI = null;

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

    #region Canvas

    public void SetCanvas(GameObject go, bool sort = true)
    {
        Canvas canvas = Util.GetOrAddComponent<Canvas>(go);
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.overrideSorting = true;

        canvas.sortingOrder = sort ? _order++ : 0;
    }

    #endregion

    #region World / Sub Item (기존 유지)

    public T MakeWorldSpaceUI<T>(Transform parent = null, string name = null)
        where T : UI_Base
    {
        name ??= typeof(T).Name;

        GameObject go = Managers.Resource.Instantiate($"UI/WorldSpace/{name}");
        if (parent != null)
            go.transform.SetParent(parent);

        Canvas canvas = go.GetOrAddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.worldCamera = Camera.main;

        return Util.GetOrAddComponent<T>(go);
    }

    public T MakeSubItem<T>(Transform parent = null, string name = null)
        where T : UI_Base
    {
        name ??= typeof(T).Name;

        GameObject go = Managers.Resource.Instantiate($"UI/SubItem/{name}");
        if (parent != null)
            go.transform.SetParent(parent);

        return Util.GetOrAddComponent<T>(go);
    }

    #endregion

    #region Scene UI

    /// <summary>
    /// 외부용 API (기존 유지)
    /// </summary>
    public void ShowSceneUI<T>(string name = null)
        where T : UI_Scene
    {
        ShowSceneUIAsync<T>(name).Forget();
    }

    /// <summary>
    /// 실제 async 로직
    /// </summary>
    private async UniTask ShowSceneUIAsync<T>(string name = null)
        where T : UI_Scene
    {
        name ??= typeof(T).Name;

        if (_uiObjects.TryGetValue(name, out GameObject existing))
        {
            existing.SetActive(true);
            _sceneUI = existing.GetComponent<T>();
            return;
        }

        string addressKey = $"UI/Scene/{name}";

        try
        {
            GameObject prefab =
                await Managers.AddressableManager.LoadAssetAsync<GameObject>(addressKey);

            GameObject go = Object.Instantiate(prefab, Root.transform);
            go.name = name;

            SetCanvas(go, true);

            _sceneUI = Util.GetOrAddComponent<T>(go);
            _uiObjects[name] = go;
        }
        catch
        {
            Debug.LogError($"[UIManager] Scene UI Load Failed : {name}");
        }
    }

    #endregion

    #region Popup UI

    public void ShowPopupUI<T>(string name = null)
        where T : UI_Popup
    {
        ShowPopupUIAsync<T>(name).Forget();
    }

    private async UniTask ShowPopupUIAsync<T>(string name = null)
        where T : UI_Popup
    {
        name ??= typeof(T).Name;

        if (_uiObjects.TryGetValue(name, out GameObject existing))
        {
            existing.SetActive(true);
            _popupStack.Push(existing.GetComponent<T>());
            return;
        }

        string addressKey = $"UI/Popup/{name}";

        try
        {
            GameObject prefab =
                await Managers.AddressableManager.LoadAssetAsync<GameObject>(addressKey);

            GameObject go = Object.Instantiate(prefab, Root.transform);
            go.name = name;

            SetCanvas(go, true);

            T popup = Util.GetOrAddComponent<T>(go);
            _popupStack.Push(popup);

            _uiObjects[name] = go;
        }
        catch
        {
            Debug.LogError($"[UIManager] Popup UI Load Failed : {name}");
        }
    }

    #endregion

    #region Close / Clear

    public bool HasPopup<T>() where T : UI_Popup
        => _popupStack.Any(p => p is T);

    public void CloseUI(string name)
    {
        if (_uiObjects.TryGetValue(name, out GameObject ui))
        {
            ui.SetActive(false);
            _order--;
        }
    }

    public void ClosePopupUI(UI_Popup popup)
    {
        if (_popupStack.Count == 0 || _popupStack.Peek() != popup)
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
            Object.Destroy(ui);

        _uiObjects.Clear();
    }

    #endregion
}
