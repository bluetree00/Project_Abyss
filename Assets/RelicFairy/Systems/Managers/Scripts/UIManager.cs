using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Cysharp.Threading.Tasks;

public class UIManager
{
    // 팝업 정렬 베이스. Canvas_HUD(100)보다 위, Canvas_Overlay(2000)보다 아래에 위치해야
    // 팝업/대사가 HUD 위에, 토스트·로딩·페이드(Overlay) 아래에 렌더된다.
    // ⚠️ 리셋(ClearOnSceneTransition)도 반드시 이 상수로 — 과거 10으로 리셋해 전환 후 대사가 HUD 뒤로 묻힌 버그.
    const int PopupBaseOrder = 200;
    int _order = PopupBaseOrder;

    Stack<UI_Popup> _popupStack = new Stack<UI_Popup>();

    // 게임플레이 차단(BlocksGameplay 팝업) 상태 — 시간정지 + 입력잠금 토글.
    bool _gameplayBlocked;
    UI_Scene _menuUI = null;

    Dictionary<string, GameObject> _uiObjects = new Dictionary<string, GameObject>();

    // @UIRoot 캔버스 루트 — AppBootstrapper에서 주입
    private Transform _sceneRoot;   // Canvas_Scene
    private Transform _popupRoot;   // Canvas_Popup
    private Transform _overlayRoot; // Canvas_Overlay
    private Transform _worldRoot;   // Canvas_WorldSpace

    // 루트가 주입된 경우 DDOL 캔버스 자식으로, 아니면 레거시 @UI_Root 사용
    private Transform SceneParent => _sceneRoot != null ? _sceneRoot : GetLegacyRoot();
    private Transform PopupParent => _popupRoot != null ? _popupRoot : GetLegacyRoot();

    private bool IsRootInjected => _sceneRoot != null;

    /// <summary>
    /// AppBootstrapper가 UIRoot 확보 후 주입
    /// </summary>
    public void SetRoots(Transform sceneRoot, Transform popupRoot,
                         Transform overlayRoot = null, Transform worldRoot = null)
    {
        _sceneRoot   = sceneRoot;
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

        // overrideSorting=true 시 부모 GraphicRaycaster가 무시되므로 자체 추가
        Util.GetOrAddComponent<UnityEngine.UI.GraphicRaycaster>(go);
    }

    #endregion

    #region World / Sub Item

    public T MakeWorldSpaceUI<T>(Transform parent = null, string name = null)
        where T : UI_Base
    {
        name ??= typeof(T).Name;

        GameObject go = Object.Instantiate(Resources.Load<GameObject>($"Prefabs/UI/WorldSpace/{name}"));
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

        GameObject go = Object.Instantiate(Resources.Load<GameObject>($"Prefabs/UI/SubItem/{name}"));
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

    #region Scene UI (Canvas_Scene / UIRoot 사전 배치 + SetActive)

    /// <summary>
    /// SceneRoot 자식에 미리 배치된 T를 찾아 활성화합니다.
    /// 이전 씬 UI는 비활성화됩니다.
    /// </summary>
    public void ShowMenuUI<T>() where T : UI_Scene
    {
        if (_sceneRoot == null)
        {
            Debug.LogError("[UIManager] SceneRoot is not set. Call SetRoots() first.");
            return;
        }

        var ui = _sceneRoot.GetComponentInChildren<T>(true);
        if (ui == null)
        {
            Debug.LogError($"[UIManager] {typeof(T).Name} not found in SceneRoot. Pre-place it in UIRoot prefab.");
            return;
        }

        if (_menuUI != null)
            _menuUI.gameObject.SetActive(false);

        ui.gameObject.SetActive(true);
        ui.Init();
        _menuUI = ui;
    }

    public void CloseMenuUI()
    {
        if (_menuUI == null) return;
        _menuUI.gameObject.SetActive(false);
        _menuUI = null;
    }

    #endregion

    #region Popup UI (Canvas_Popup)

    public void ShowPopupUI<T>(string name = null)
        where T : UI_Popup
    {
        ShowPopupUIAndGetAsync<T>(name).Forget();
    }

    /// <summary>
    /// 팝업을 열고 인스턴스를 반환한다. 직접 제어가 필요한 경우(장비 교체 등) 사용.
    /// </summary>
    public async UniTask<T> ShowPopupUIAndGetAsync<T>(string name = null)
        where T : UI_Popup
    {
        name ??= typeof(T).Name;

        if (_uiObjects.TryGetValue(name, out GameObject existing))
        {
            if (existing != null)
            {
                existing.SetActive(true);
                var cached = existing.GetComponent<T>();
                _popupStack.Push(cached);
                RefreshGameplayBlock();
                cached.PlayOpenAnimation();
                return cached;
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
            RefreshGameplayBlock();
            _uiObjects[name] = go;

            popup.PlayOpenAnimation();
            return popup;
        }
        catch
        {
            Debug.LogError($"[UIManager] Popup UI Load Failed : {name}");
            return null;
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
        CloseTopPopup(immediate: false);
    }

    public void ClosePopupUI()
    {
        CloseTopPopup(immediate: false);
    }

    /// <param name="immediate">true면 애니메이션 없이 즉시 파괴 (씬 전환, 일괄 닫기용).</param>
    private void CloseTopPopup(bool immediate)
    {
        if (_popupStack.Count == 0) return;

        UI_Popup popup = _popupStack.Pop();
        _uiObjects.Remove(popup.gameObject.name);
        _order--;
        RefreshGameplayBlock();

        if (immediate)
            Object.Destroy(popup.gameObject);
        else
            popup.StartCloseAndDestroy();
    }

    public void CloseAllPopupUI()
    {
        while (_popupStack.Count > 0)
            CloseTopPopup(immediate: true);
    }

    // ── 게임플레이 차단(시간정지 + 입력잠금) ──────────────────

    /// <summary>BlocksGameplay 팝업이 하나라도 열려있나.</summary>
    public bool IsGameplayBlocked => _popupStack.Any(p => p != null && p.BlocksGameplay);

    /// <summary>BlocksGameplay 팝업이 모두 닫힐 때까지 대기. 대사를 선택 UI 닫힘 뒤로 미루는 데 사용.</summary>
    public async UniTask WaitUntilNoBlockingPopupAsync()
        => await UniTask.WaitUntil(() => !IsGameplayBlocked);

    /// <summary>스택의 BlocksGameplay 팝업 유무에 따라 시간정지·입력잠금을 동기화한다(push/pop마다 호출).</summary>
    private void RefreshGameplayBlock()
    {
        bool shouldBlock = IsGameplayBlocked;
        if (shouldBlock == _gameplayBlocked) return;
        _gameplayBlocked = shouldBlock;

        if (shouldBlock)
        {
            TimeScaleArbiter.Acquire(this, 0f, TimeScaleArbiter.Priority.Pause);
            SetPlayerInput(false);
        }
        else
        {
            TimeScaleArbiter.Release(this);
            SetPlayerInput(true);
        }
    }

    private void SetPlayerInput(bool enabled)
    {
        var t = Managers.Player?.PlayerTransform;
        if (t != null && t.TryGetComponent<PlayerController>(out var pc))
            pc.SetInputEnabled(enabled);
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
        _order = PopupBaseOrder;

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
