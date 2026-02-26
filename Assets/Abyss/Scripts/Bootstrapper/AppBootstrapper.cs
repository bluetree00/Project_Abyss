using System;
using System.Collections.Generic;
using UnityEngine;
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
    [SerializeField] private Define.Scene startScene = Define.Scene.Title;
    [SerializeField] private GameFlowState startState = GameFlowState.Title;

    public bool IsReady { get; private set; }

    private GameFlowManager _flow;
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

        // 4) (선택) UIRoot 확보
        if (autoCreateUIRoot)
            await EnsureUIRootAsync();

        // 5) (선택) Flow 시작 (SceneTransitionManager 바인딩 필수)
        if (startFlow)
        {
            _flow = new GameFlowManager();
            _scene = new SceneTransitionManager(this);

            // ✅ 너가 이전에 쓴 방식이 Bind가 있는 구조라면 반드시 연결
            _flow.BindSceneTransition(_scene);

            // ✅ 한 번만 호출
            _flow.RequestLoad(startScene, startState);
        }

        IsReady = true;
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