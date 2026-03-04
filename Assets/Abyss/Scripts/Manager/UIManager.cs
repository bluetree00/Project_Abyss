using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Cysharp.Threading.Tasks;

public class UIManager
{
    int _order = 10;

    Stack<UI_Popup> _popupStack = new Stack<UI_Popup>();
    UI_Scene _menuUI = null;

    Dictionary<string, GameObject> _uiObjects = new Dictionary<string, GameObject>();

    // @UIRoot 캔버스 루트 — AppBootstrapper에서 주입
    private Transform _menuRoot;
    private Transform _popupRoot;
    private Transform _overlayRoot;
    private Transform _worldRoot;

    // 루트가 주입된 경우 DDOL 캔버스 자식으로, 아니면 레거시 @UI_Root 사용
    private Transform MenuParent  => _menuRoot  != null ? _menuRoot  : GetLegacyRoot();
    private Transform PopupParent => _popupRoot != null ? _popupRoot : GetLegacyRoot();

    private bool IsRootInjected => _menuRoot != null;

    /// <summary>
    /// AppBootstrapper가 UIRoot 확보 후 주입
    /// </summary>
    public void SetRoots(Transform menuRoot, Transform popupRoot,
                         Transform overlayRoot = null, Transform worldRoot = null)
    {
        _menuRoot    = menuRoot;
        _popupRoot   = popupRoot;
        _overlayRoot = overlayRoot;
        _worldRoot   = worldRoot;
    }

    /// <summary>
    /// 레거시 폴백: @UIRoot 미사용 환경(테스트 씬 등)에서 자체 Root 생성
    /// </summary>
    private Transform GetLegacyRoot()
    {
        GameObject root = GameObject.Find("@UI_Root");
        if (root == null)
            root = new GameObject { name = "@UI_Root" };
        return root.transform;
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

    #region World / Sub Item

    public T MakeWorldSpaceUI<T>(Transform parent = null, string name = null)
        where T : UI_Base
    {
        name ??= typeof(T).Name;

        GameObject go = Managers.Resource.Instantiate($"UI/WorldSpace/{name}");
        Transform target = parent != null ? parent : _worldRoot;
        if (target != null)
            go.transform.SetParent(target);

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

    #region Overlay UI (사전 배치 + SetActive)

    /// <summary>
    /// OverlayRoot 자식에서 사전 배치된 UI를 검색합니다.
    /// </summary>
    public T GetOverlayUI<T>() where T : UI_Base
    {
        if (_overlayRoot == null) return null;
        return _overlayRoot.GetComponentInChildren<T>(true);
    }

    public void ShowOverlayUI<T>() where T : UI_Base
    {
        GetOverlayUI<T>()?.gameObject.SetActive(true);
    }

    public void HideOverlayUI<T>() where T : UI_Base
    {
        GetOverlayUI<T>()?.gameObject.SetActive(false);
    }

    #endregion

    #region Menu UI (Canvas_Menu / 런타임 생성)

    public void ShowMenuUI<T>(string name = null)
        where T : UI_Scene
    {
        ShowMenuUIAsync<T>(name).Forget();
    }

    private async UniTask ShowMenuUIAsync<T>(string name = null)
        where T : UI_Scene
    {
        name ??= typeof(T).Name;

        if (_uiObjects.TryGetValue(name, out GameObject existing))
        {
            if (existing != null)
            {
                existing.SetActive(true);
                _menuUI = existing.GetComponent<T>();
                return;
            }
            _uiObjects.Remove(name);
        }

        string addressKey = $"UI/Menu/{name}";

        try
        {
            GameObject prefab =
                await Managers.AddressableManager.LoadAssetAsync<GameObject>(addressKey);

            GameObject go = Object.Instantiate(prefab, MenuParent);
            go.name = name;

            _menuUI = Util.GetOrAddComponent<T>(go);
            _menuUI.Init();
            _uiObjects[name] = go;
        }
        catch
        {
            Debug.LogError($"[UIManager] Menu UI Load Failed : {name}");
        }
    }

    public void CloseMenuUI()
    {
        if (_menuUI == null) return;
        _menuUI.Close();
        _menuUI = null;
    }

    #endregion

    #region Popup UI (Canvas_Popup / 런타임 생성)

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
            if (existing != null)
            {
                existing.SetActive(true);
                _popupStack.Push(existing.GetComponent<T>());
                return;
            }
            _uiObjects.Remove(name);
        }

        string addressKey = $"UI/Popup/{name}";

        try
        {
            GameObject prefab =
                await Managers.AddressableManager.LoadAssetAsync<GameObject>(addressKey);

            GameObject go = Object.Instantiate(prefab, PopupParent);
            go.name = name;

            T popup = Util.GetOrAddComponent<T>(go);
            popup.Init();
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
        if (_uiObjects.TryGetValue(name, out GameObject ui) && ui != null)
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
        _menuUI = null;
    }

    /// <summary>
    /// GameFlow 상태 전환 시 호출
    /// - @UIRoot 주입 모드: UI 오브젝트가 DDOL이므로 캐시 유지, 화면만 닫음
    /// - 레거시 모드: @UI_Root가 씬 전환 시 소멸되므로 캐시도 초기화
    /// </summary>
    public void ClearOnSceneTransition()
    {
        CloseAllPopupUI();
        CloseMenuUI();
        _order = 10;

        if (!IsRootInjected)
            _uiObjects.Clear();
    }

    public void ClearAllUI()
    {
        foreach (var ui in _uiObjects.Values)
        {
            if (ui != null)
                Object.Destroy(ui);
        }

        _uiObjects.Clear();
    }

    #endregion
}
