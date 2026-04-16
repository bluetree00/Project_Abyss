using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using Cysharp.Threading.Tasks;
using BackEnd;

public sealed class AppBootstrapper : MonoBehaviour
{
    public static AppBootstrapper Instance { get; private set; }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void AutoCreate()
    {
        if (FindObjectOfType<AppBootstrapper>(true) != null)
            return;

        var go = new GameObject("@AppBootstrapper");
        go.AddComponent<AppBootstrapper>();
    }
#endif

    [Header("Core Init")]
    [SerializeField] private bool initBackend = true;
    [SerializeField] private bool initAddressables = true;

    public static bool IsBackendInitialized { get; private set; }
    public static bool IsAutoLoggedIn { get; private set; }

    [Header("Animation Preload (Test Friendly)")]
    [SerializeField] private bool preloadAnimations = true;

    [Header("UIRoot Auto Create")]
    [SerializeField] private bool autoCreateUIRoot = true;
    [SerializeField] private string uiRootPrefabKey = "@UIRoot";
    private bool _uiRootEnsured;

    [Header("Steam Login")]
    [SerializeField] private bool useSteamLogin = false;

    [Header("Auto Login (Device ID)")]
    [SerializeField] private bool useAutoLogin = true;

    [Header("Flow Start (Optional)")]
    [SerializeField] private bool startFlow = false;   // 테스트 씬이면 보통 false
    [SerializeField] private Define.Scene startScene = Define.Scene.Logo;
    public bool IsReady { get; private set; }

    // ---- 로드아웃 (로비 선택 → InGame 전달) ----
    public PlayerLoadout Loadout { get; private set; } = new PlayerLoadout();

    // ---- Run 수명 관리 ----
    public GameRunSession CurrentRun { get; private set; }

    public void BeginRun(GameRunSession session)
    {
        CurrentRun = session;
    }

    public void EndRun()
    {
        CurrentRun = null;
        Loadout.Clear();
    }

    public void RequestLoad(Define.Scene scene)
    {
        if (_flow != null)
        {
            _flow.RequestLoad(scene);
            return;
        }

        // startFlow = false 환경(테스트 씬): UI 정리 후 비동기 로드
        Managers.UI.ClearOnSceneTransition();
        LoadSceneNoFlowAsync(scene).Forget();
    }

    private async UniTaskVoid LoadSceneNoFlowAsync(Define.Scene scene)
    {
        var loading = UI_SceneLoading.Instance;
        if (loading != null) await loading.ShowAsync();

        var op = SceneManager.LoadSceneAsync(scene.ToString());
        op.allowSceneActivation = false;

        float speed = loading != null ? loading.ProgressSpeed : 0.5f;
        float display = 0f;
        while (op.progress < 0.9f)
        {
            display = Mathf.MoveTowards(display, op.progress / 0.9f, Time.unscaledDeltaTime * speed);
            loading?.SetProgress(display);
            await UniTask.Yield(PlayerLoopTiming.Update);
        }
        // 표시 진행도를 1까지 천천히 채운 뒤 씬 활성화
        while (display < 1f)
        {
            display = Mathf.MoveTowards(display, 1f, Time.unscaledDeltaTime * speed);
            loading?.SetProgress(display);
            await UniTask.Yield(PlayerLoopTiming.Update);
        }
        op.allowSceneActivation = true;
        await UniTask.WaitUntil(() => op.isDone);
    }

    public void NotifySceneReady()
    {
        UI_SceneLoading.Instance?.HideAsync().Forget();
    }

    public void RequestStartRun()
    {
        RequestLoad(Define.Scene.StageMap);
    }

    private GameFlow _flow;
    private SceneTransitionManager _scene;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);

        if (initBackend && !IsBackendInitialized)
        {
            var bro = Backend.Initialize();
            if (bro.IsSuccess())
            {
                IsBackendInitialized = true;
                Debug.Log("[AppBootstrapper] Backend initialized.");
            }
            else
            {
                Debug.LogError($"[AppBootstrapper] Backend initialize failed: {bro.GetMessage()}");
            }
        }

        SystemSetup();
    }

    private static void SystemSetup()
    {
        Application.runInBackground = true;
        Screen.sleepTimeout = SleepTimeout.NeverSleep;

        int width = Screen.width;
        int height = (int)(Screen.width * 9f / 16f);
        Screen.SetResolution(width, height, true);
    }

    private async void Start()
    {
        // 1) Managers 준비 보장 (서비스 로케이터)
        var mgr = Managers.Instance;
        if (mgr == null)
        {
            Debug.LogError("[AppBootstrapper] Managers.Instance is null. (IsQuitting flag?)");
            return;
        }

        // 2) 뒤끝 초기화
        if (initBackend && !IsBackendInitialized)
        {
            var bro = Backend.Initialize();
            if (bro.IsSuccess())
            {
                IsBackendInitialized = true;
                Debug.Log("[AppBootstrapper] Backend initialized.");
            }
            else
            {
                Debug.LogError($"[AppBootstrapper] Backend initialize failed: {bro.GetMessage()}");
            }
        }

        // 3) Addressables 초기화
        if (initAddressables)
        {
            var addr = Managers.AddressableManager;
            if (addr == null)
            {
                Debug.LogError("[AppBootstrapper] Managers.AddressableManager is null.");
                return;
            }

            await addr.InitAsync();
        }

        // 5) (선택) UIRoot 확보 + UIManager에 캔버스 루트 주입
        if (autoCreateUIRoot)
        {
            await EnsureUIRootAsync();

            var uiRoot = UIRootBootstrapper.Instance;
            if (uiRoot != null)
                Managers.UI.SetRoots(uiRoot.SceneRoot, uiRoot.PopupRoot, uiRoot.OverlayRoot, uiRoot.WorldRoot);
            else
                Debug.LogWarning("[AppBootstrapper] UIRootBootstrapper not found. UIManager will use legacy root.");
        }

        // 6) (선택) Steam 로그인 — 성공 시 Login씬 스킵, Lobby로 직행
        if (useSteamLogin)
        {
            var steamGo = new GameObject("@SteamManager");
            steamGo.AddComponent<SteamManager>();

            bool steamOk = await SteamLoginService.LoginAsync();
            if (!steamOk)
            {
                Debug.LogError("[AppBootstrapper] Steam 로그인 실패. 게임을 시작할 수 없습니다.");
                return;
            }

            startScene = Define.Scene.Lobby;
        }

        // 6-b) 디바이스 ID 자동 로그인 — Login 씬 스킵
        if (useAutoLogin && !useSteamLogin)
        {
            bool autoOk = await DeviceAutoLoginAsync();
            if (autoOk)
            {
                IsAutoLoggedIn = true;
                Debug.Log("[AppBootstrapper] 자동 로그인 성공 → Login 스킵");
                if (startScene == Define.Scene.Login || startScene == Define.Scene.Logo)
                    startScene = Define.Scene.Lobby;
            }
            else
            {
                Debug.LogWarning("[AppBootstrapper] 자동 로그인 실패 → Login 씬으로 이동");
            }
        }

        // 7) (선택) Flow 시작 (SceneTransitionManager 바인딩 필수)
        if (startFlow)
        {
            _flow = new GameFlow();
            _scene = new SceneTransitionManager(this);

            _flow.BindSceneTransition(_scene);
            _flow.OnStateChanged += OnFlowStateChanged;
            _flow.RequestLoad(startScene);
        }
        else
        {
            // startFlow = false 일 때 (테스트 씬 직접 실행):
            // 씬 전환마다 UI를 자동 활성화
            SceneManager.sceneLoaded += OnSceneLoadedNoFlow;
            AutoShowUIForCurrentScene();
        }

        IsReady = true;
    }

    private void OnDestroy()
    {
        SceneManager.sceneLoaded -= OnSceneLoadedNoFlow;
    }

    private void OnSceneLoadedNoFlow(UnityEngine.SceneManagement.Scene scene, LoadSceneMode mode)
    {
        AutoShowUIForCurrentScene();
    }

    private void OnFlowStateChanged(GameFlowState state)
    {
        Managers.UI.ClearOnSceneTransition();
        ApplyUIForState(state);
    }

    /// <summary>
    /// startFlow = false 환경(테스트 씬 직접 실행)에서
    /// 현재 씬 이름을 기반으로 UI를 자동 활성화합니다.
    /// 씬→상태 매핑은 GameFlow.TryGetStateForScene을 재사용합니다.
    /// </summary>
    private void AutoShowUIForCurrentScene()
    {
        var sceneName = SceneManager.GetActiveScene().name;
        if (!Enum.TryParse<Define.Scene>(sceneName, out var scene))
            return;

        if (GameFlow.TryGetStateForScene(scene, out var state))
            ApplyUIForState(state);
    }

    private void ApplyUIForState(GameFlowState state)
    {
        switch (state)
        {
            case GameFlowState.Logo:
                Managers.UI.ShowMenuUI<UI_Logo>();
                NotifySceneReady();
                break;

            case GameFlowState.Login:
                Managers.UI.ShowMenuUI<UI_Login>();
                NotifySceneReady();
                break;

            case GameFlowState.Lobby:
                Managers.UI.ShowMenuUI<UI_Lobby>();
                NotifySceneReady();
                break;

            case GameFlowState.StageMap:
                Managers.UI.ShowMenuUI<UI_StageMap>();
                // StageMapBootstrapper가 비동기 초기화 완료 후 NotifySceneReady() 호출
                break;

            case GameFlowState.InGame:
                // GameRunBootstrapper가 비동기 초기화 완료 후 NotifySceneReady() 호출
                break;

            case GameFlowState.Result:
                Managers.UI.ShowMenuUI<UI_Result>();
                NotifySceneReady();
                break;
        }
    }

    /// <summary>
    /// 디바이스 고유 ID로 자동 로그인.
    /// 계정 없으면 자동 회원가입 후 재로그인.
    /// </summary>
    private static async UniTask<bool> DeviceAutoLoginAsync()
    {
        string deviceId = SystemInfo.deviceUniqueIdentifier;
        // 뒤끝 ID 제한에 맞게 접두사 + 해시
        string id = "dev_" + deviceId;
        if (id.Length > 20) id = id.Substring(0, 20);
        string pw = deviceId;
        if (pw.Length > 20) pw = pw.Substring(0, 20);

        Debug.Log($"[AutoLogin] 디바이스 로그인 시도: {id}");

        // 1차: 로그인 시도
        var loginResult = await TryCustomLoginAsync(id, pw);
        if (loginResult) return true;

        // 2차: 계정 없으면 회원가입
        Debug.Log("[AutoLogin] 계정 없음 → 회원가입 시도");
        var signupResult = Backend.BMember.CustomSignUp(id, pw);
        if (!signupResult.IsSuccess())
        {
            Debug.LogError($"[AutoLogin] 회원가입 실패: {signupResult.GetMessage()}");
            return false;
        }

        // 3차: 재로그인
        return await TryCustomLoginAsync(id, pw);
    }

    private static UniTask<bool> TryCustomLoginAsync(string id, string pw)
    {
        var tcs = new UniTaskCompletionSource<bool>();

        Backend.BMember.CustomLogin(id, pw, callback =>
        {
            if (callback.IsSuccess())
            {
                Debug.Log($"[AutoLogin] 로그인 성공: gamerId={Backend.BMember.GetUserInfo()?.GetReturnValuetoJSON()?["row"]?["gamerId"]}");
                tcs.TrySetResult(true);
            }
            else
            {
                Debug.LogWarning($"[AutoLogin] 로그인 실패: {callback.GetStatusCode()} {callback.GetMessage()}");
                tcs.TrySetResult(false);
            }
        });

        return tcs.Task;
    }

    private async UniTask EnsureUIRootAsync()
    {
        if (_uiRootEnsured)
            return;

        var existing = FindObjectOfType<UIRootBootstrapper>(true);
        if (existing != null)
        {
            _uiRootEnsured = true;
            return;
        }

        Debug.LogWarning("[AppBootstrapper] UIRoot not found. Creating from Addressables...");

        var addr = Managers.AddressableManager; 
        if (addr == null)
        {
            Debug.LogError("[AppBootstrapper] EnsureUIRootAsync failed: AddressableManager is null.");
            return;
        }

        GameObject uiRootPrefab;
        try
        {
            uiRootPrefab = await addr.LoadAssetAsync<GameObject>(uiRootPrefabKey);
        }
        catch (Exception e)
        {
            Debug.LogError($"[AppBootstrapper] Failed to load UIRoot prefab. key={uiRootPrefabKey}\n{e}");
            return;
        }

        if (uiRootPrefab == null)
        {
            Debug.LogError($"[AppBootstrapper] UIRoot prefab is null. key={uiRootPrefabKey}");
            return;
        }

        var go = Instantiate(uiRootPrefab);
        go.name = "@UIRoot";
        DontDestroyOnLoad(go);

        _uiRootEnsured = true;
        Debug.Log("[AppBootstrapper] UIRoot created successfully");
    }

}