using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using Cysharp.Threading.Tasks;

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
    [SerializeField] private bool initAddressables = true;

    [Header("Animation Preload (Test Friendly)")]
    [SerializeField] private bool preloadAnimations = true;

    [Header("UIRoot Auto Create")]
    [SerializeField] private bool autoCreateUIRoot = true;
    [SerializeField] private string uiRootPrefabKey = "@UIRoot";
    private bool _uiRootEnsured;

    [Header("Flow Start (Optional)")]
    [SerializeField] private bool startFlow = false;   // 테스트 씬이면 보통 false
    [SerializeField] private Define.Scene startScene = Define.Scene.Logo;
    public bool IsReady { get; private set; }

    // ---- Run 수명 관리 ----
    public GameRunSession CurrentRun { get; private set; }

    public void BeginRun(GameRunSession session)
    {
        CurrentRun = session;
    }

    public void EndRun()
    {
        CurrentRun = null;
    }

    public void RequestLoad(Define.Scene scene)
    {
        if (_flow != null)
        {
            _flow.RequestLoad(scene);
            return;
        }

        // startFlow = false 환경(테스트 씬): UI 정리 후 SceneManager 직접 로드
        Managers.UI.ClearOnSceneTransition();
        SceneManager.LoadScene(scene.ToString());
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
    }

    private async void Start()
    {
        // 1) Managers 준비 보장
        var mgr = Managers.Instance;
        if (mgr == null)
        {
            Debug.LogError("[AppBootstrapper] Managers.Instance is null. (IsQuitting flag?)");
            return;
        }

        // 2) Addressables 초기화
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

        // 3) (선택) 애니메이션 선로딩
        if (preloadAnimations)
            await PreloadAnimationsAsync();

        // 4) (선택) UIRoot 확보 + UIManager에 캔버스 루트 주입
        if (autoCreateUIRoot)
        {
            await EnsureUIRootAsync();

            var uiRoot = UIRootBootstrapper.Instance;
            if (uiRoot != null)
                Managers.UI.SetRoots(uiRoot.SceneRoot, uiRoot.PopupRoot, uiRoot.OverlayRoot, uiRoot.WorldRoot);
            else
                Debug.LogWarning("[AppBootstrapper] UIRootBootstrapper not found. UIManager will use legacy root.");
        }

        // 5) (선택) Flow 시작 (SceneTransitionManager 바인딩 필수)
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
                // 로고 연출 처리 (별도 LogoBootstrapper 또는 씬 자체에서 처리)
                break;

            case GameFlowState.Login:
                Managers.UI.ShowMenuUI<UI_Login>();
                break;

            case GameFlowState.Lobby:
                Managers.UI.ShowMenuUI<UI_Lobby>();
                break;

            case GameFlowState.StageMap:
                Managers.UI.ShowMenuUI<UI_StageMap>();
                break;

            case GameFlowState.InGame:
                // HUD 바인딩은 GameRunBootstrapper → HudBootstrapper에서 처리
                break;

            case GameFlowState.Result:
                Managers.UI.ShowMenuUI<UI_Result>();
                break;
        }
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

    private async UniTask PreloadAnimationsAsync()
    {
        var animKeys = new List<string>
        {
            "GroundLightAttack_01",
            "GroundLightAttack_02",
            "GroundLightAttack_03",
            "ESkill_01",
            "QSkill_01",
            "Air_Light_Step01_Start",
            "Air_Light_Step01_Loop",
            "Air_Light_Step01_End",
            "GroundLightBowAttack_01",
            "GroundLightBowAttack_02",
            "HeavyCharge",
            "GroundHeavyAttack",
            "BowHeavyCharge",
        };

        try
        {
            var anim = Managers.AnimationResources;
            if (anim == null)
            {
                Debug.LogError("[AppBootstrapper] AnimationResources is null.");
                return;
            }

            await anim.PreloadClipsAsync(animKeys);
            Debug.Log("[AppBootstrapper] AnimationResourceManager preload 완료");
        }
        catch (Exception e)
        {
            Debug.LogError($"[AppBootstrapper] Animation preload 실패: {e}");
        }
    }
}