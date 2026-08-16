using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Cysharp.Threading.Tasks;

public class UIManager
{
    // 팝업 정렬 베이스. Canvas_HUD(100)보다 위, Canvas_Overlay(2000)보다 아래에 위치해야
    // 팝업/대사가 HUD 위에, 토스트·로딩·페이드(Overlay) 아래에 렌더된다.
    // ⚠️ 리셋(ClearOnSceneTransition)도 반드시 이 상수로 — 과거 10으로 리셋해 전환 후 대사가 HUD 뒤로 묻힌 버그.
    const int PopupBaseOrder = UISortingOrder.PopupStack;
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
    /// 레거시 폴백: @UIRoot 미사용 환경(테스트 씬 등)에서 자체 Root 생성.
    /// ⚠️ 과거엔 Canvas 없는 빈 GameObject였다 — 팝업이 자기 Canvas로 홀로 서면서
    /// CanvasScaler가 없어 Constant Pixel Size로 동작(저해상도 잘림/고해상도 축소).
    /// 프로젝트 표준(1920×1080 / ScaleWithScreenSize / Match 0.5) 캔버스를 부착한다.
    /// </summary>
    private Transform GetLegacyRoot()
    {
        GameObject root = GameObject.Find("@UI_Root");
        if (root == null)
            root = new GameObject { name = "@UI_Root" };

        Canvas canvas = Util.GetOrAddComponent<Canvas>(root);
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;

        var scaler = Util.GetOrAddComponent<UnityEngine.UI.CanvasScaler>(root);
        scaler.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.screenMatchMode = UnityEngine.UI.CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;

        Util.GetOrAddComponent<UnityEngine.UI.GraphicRaycaster>(root);

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

        // Init은 '구조 바인딩 1회'다. @UIRoot는 DDOL이라 같은 인스턴스가 다시 보여지는데,
        // 매번 Init을 부르면 onClick 리스너가 겹겹이 쌓여 버튼 1회 클릭이 N번 실행됐다.
        // 표시할 때마다 되돌려야 하는 상태는 각 UI의 OnEnable이 담당한다.
        if (!ui.IsInitialized)
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

                // 이미 스택에 있는 인스턴스는 다시 push하지 않는다.
                // 같은 인스턴스가 두 겹 쌓이면 닫기 1회로는 한 겹만 빠져 좀비 항목이 남고,
                // 그 항목 때문에 ESC가 먹히지 않거나 게임플레이 차단(시간정지)이 안 풀린다.
                if (cached != null && _popupStack.Contains(cached))
                    return cached;

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

    /// <summary>
    /// ESC 공용 닫기 — 스택 최상단 팝업이 CloseOnEscape면 그것만 닫는다.
    /// 반환 true = ESC를 소비했음(호출측은 기존 ESC 동작을 실행하면 안 된다).
    /// 팝업이 열려있기만 하면(닫을 수 없는 팝업이어도) ESC는 소비된다 — 팝업 위로 ESC가
    /// 흘러 다른 UI가 열리는 것을 막는다.
    /// 중첩 팝업에서 최상단 1개만 닫히도록 Peek만 본다.
    /// </summary>
    public bool TryCloseTopPopupOnEscape()
    {
        // 파괴됐는데 아직 pop되지 않은 '좀비' 항목을 먼저 걷어낸다.
        // 남겨두면 Peek()이 계속 null을 돌려주고 ESC는 true(소비)로만 끝나 아무 것도 닫히지 않는다
        // — 일시정지도 못 열고 팝업도 못 닫는 소프트락. CloseTopPopup이 null 항목을 안전하게 처리한다.
        while (_popupStack.Count > 0 && _popupStack.Peek() == null)
            CloseTopPopup(immediate: true);

        if (_popupStack.Count == 0) return false;

        UI_Popup top = _popupStack.Peek();
        if (top.CloseOnEscape)
            top.ClosePopupUI();

        return true;
    }

    /// <param name="immediate">true면 애니메이션 없이 즉시 파괴 (씬 전환, 일괄 닫기용).</param>
    private void CloseTopPopup(bool immediate)
    {
        if (_popupStack.Count == 0) return;

        UI_Popup popup = _popupStack.Pop();
        _order--;

        // 이미 파괴된 팝업(씬 전환 중 자체 파괴 등)은 스택에서 빼기만 한다.
        // gameObject 접근 시 MissingReferenceException이 나므로 반드시 먼저 검사.
        if (popup == null)
        {
            RefreshGameplayBlock();
            return;
        }

        _uiObjects.Remove(popup.gameObject.name);
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

    /// <summary>
    /// 차단형 팝업이 정상 경로 밖에서 파괴됐을 때 <see cref="UI_Popup.OnDestroy"/>가 호출.
    /// 스택에 남은 항목은 파괴돼 null이므로 재평가만으로 차단이 풀린다(스택 정리는 다음 pop이 처리).
    /// </summary>
    public void RefreshGameplayBlockExternally() => RefreshGameplayBlock();

    /// <summary>스택의 BlocksGameplay 팝업 유무에 따라 시간정지·입력잠금을 동기화한다(push/pop마다 호출).</summary>
    private void RefreshGameplayBlock()
    {
        bool shouldBlock = IsGameplayBlocked;
        if (shouldBlock == _gameplayBlocked) return;
        _gameplayBlocked = shouldBlock;

        if (shouldBlock)
        {
            // [진단] 시간정지를 거는 '차단 팝업'이 무엇인지 남긴다 — 안 닫혀 고착되는 범인 특정용.
            var sb = new System.Text.StringBuilder();
            foreach (var p in _popupStack)
                if (p != null && p.BlocksGameplay) sb.Append(p.GetType().Name).Append(' ');
            Debug.LogWarning($"[UIManager] 게임플레이 차단 ON(시간정지). 차단 팝업: {sb}");

            TimeScaleArbiter.Acquire(this, 0f, TimeScaleArbiter.Priority.Pause);
            SetPlayerInput(false);
        }
        else
        {
            TimeScaleArbiter.Release(this);
            SetPlayerInput(true);
        }
    }

    /// <summary>
    /// 팝업 차단 전용 입력 채널(SetUiBlocked). 컷신이 건 차단(SetInputEnabled)과 독립이라
    /// 팝업이 닫혀도 컷신 차단을 덮어쓰지 않는다 — 컷신 중 대사 팝업이 닫히면 조작이 되살아나던 문제.
    /// </summary>
    private void SetPlayerInput(bool enabled)
    {
        var t = Managers.Player?.PlayerTransform;
        if (t != null && t.TryGetComponent<PlayerController>(out var pc))
            pc.SetUiBlocked(!enabled);
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

        // 룬판(UI_GridPanel)은 UI_Popup이 아니라 팝업 스택에 없다 — CloseAllPopupUI가 못 닫는다.
        // 열린 채 씬이 넘어가면 OpenPanel의 TimeScaleArbiter.Acquire(0)가 풀리지 않아 시간이 고착된다.
        // Close()가 Release + ExitGridSynergy까지 수행하므로 열려 있을 때만 호출한다.
        if (UI_GridPanel.Instance != null && UI_GridPanel.Instance.IsOpen)
            UI_GridPanel.Instance.Close();

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
