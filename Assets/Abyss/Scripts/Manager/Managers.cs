using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.AddressableAssets;
using Cysharp.Threading.Tasks;

/// <summary>
/// 게임 전체에서 사용할 싱글톤 매니저 집합.
/// - 데이터 매니저, 코어 매니저, 플레이어/몬스터/스테이지 등 관리.
/// - MonoBehaviour 기반으로 Awake 시 인스턴스 생성, DontDestroyOnLoad.
/// </summary>
public class Managers : MonoBehaviour
{
    #region Singleton
    private static Managers s_instance;
    public static Managers Instance
    {
        get
        {
            if (s_instance == null)
            {
                GameObject go = GameObject.Find("@Managers");
                if (go == null)
                {
                    go = new GameObject { name = "@Managers" };
                    go.AddComponent<Managers>();
                }
                s_instance = go.GetComponent<Managers>();
            }
            return s_instance;
        }
    }
    #endregion

    #region Core Managers
    // Input, Resource, Pool, Addressables 등 게임 코어
    private InputManager _input;
    private ResourceManager _resource;
    private ObjectPoolerManager _objectPoolerManager;
    private AddressableManager _addressableManager;
    private AnimationResourceManager _animationResources;

    // UI, 데이터, 이벤트 관련
    private UIManager _ui;
    private CharacterDataManager _characterDataManager;
    private GameEventManager _gameEventManager;

    [Header("UIRoot Auto Create")]
    [SerializeField] private bool autoCreateUIRoot = true;
    [SerializeField] private string uiRootPrefabKey = "@UIRoot";

     private bool _uiRootEnsured;

    // 플레이어, 씬, 데이터, 몬스터
    private PlayerManager _playerManager = new PlayerManager();
    private SceneManagerEx _scene = new SceneManagerEx();
    private MonsterDataManager _monsterDataManager;

    // Core manager static accessors
    public static InputManager Input => Instance._input ??= new InputManager();
    public static ResourceManager Resource => Instance._resource ??= new ResourceManager();
    public static ObjectPoolerManager ObjectPooler => Instance._objectPoolerManager;
    public static AddressableManager AddressableManager => Instance._addressableManager ??= new AddressableManager();
    public static AnimationResourceManager AnimationResources => Instance._animationResources ??= new AnimationResourceManager();
    public static UIManager UI => Instance._ui ??= new UIManager();
    public static CharacterDataManager CharacterData => Instance._characterDataManager ??= new CharacterDataManager();
    public static GameEventManager GameEvent => Instance._gameEventManager ??= new GameEventManager();
    public static PlayerManager Player => Instance._playerManager;
    public static SceneManagerEx Scene => Instance._scene;
    public static MonsterDataManager MonsterData => Instance._monsterDataManager ??= new MonsterDataManager();
    #endregion

    #region GameRun Manager
    // Run 단위 매니저: Stage, Player, Run 상태 추적
    private GameRunManager _gameRunManager;
    public static GameRunManager GameRun => Instance._gameRunManager ??= new GameRunManager();

    [Header("Bootstrapper Auto-Create (Safety Net)")]
    [SerializeField] private bool autoCreateRunBootstrapper = true;
    [SerializeField] private bool logAutoCreateWarning = true;
    #endregion

    #region Unity Callbacks
    private void Awake()
    {
        // 싱글톤 처리
        if (s_instance == null)
        {
            s_instance = this;
            DontDestroyOnLoad(this);
        }
        else
        {
            Destroy(gameObject);
            return;
        }

        // 코어 매니저 초기화
        _addressableManager = new AddressableManager();

        _objectPoolerManager = new ObjectPoolerManager();

        // Addressables 초기화 후 애니메이션 리소스 초기화
        InitializeAddressablesAsync().ContinueWith(async () =>
        {
            await InitializeAnimationsAsync();
        }).Forget();

        // 첫 씬에서도 바로 확보(에디터 플레이 시 sceneLoaded 전에 필요할 때 대비)
        EnsureGameRunBootstrapperInScene();
    }

    private void Update()
    {
        // 1. InputManager 업데이트 호출
        _input?.OnUpdate();

        // 2. ESC 키: 일시정지 UI
        if (_input != null && _input.GetKeyDown(KeyCode.Escape))
        {
            if (!UI.HasPopup<UI_Pause>())
            {
                Time.timeScale = 0f;
                UI.ShowPopupUI<UI_Pause>();
            }
        }
    }

     // -------------------------
    // 핵심: Bootstrapper 확보
    // -------------------------
     private void EnsureGameRunBootstrapperInScene()
    {
        if (!autoCreateRunBootstrapper)
            return;

        var existing = FindObjectOfType<GameRunBootstrapper>(true);
        if (existing != null)
            return;

        if (logAutoCreateWarning)
            Debug.LogWarning("[Managers] GameRunBootstrapper not found in scene. Auto-created.");

        var go = new GameObject("@GameRunBootstrapper");
        go.AddComponent<GameRunBootstrapper>(); // Awake에서 Bind() 호출
    }

      private async void Start()
    {
        // Addressables 초기화 보장
        await AddressableManager.InitAsync();

        // GameRunBootstrapper 안전망 (기존 코드 유지)
        EnsureGameRunBootstrapperInScene();

        // UIRoot 확보
        await EnsureUIRootAsync();
    }

    /// <summary>
    /// 씬에 UIRootBootstrapper가 없으면 Addressables에서 로드 후 생성
    /// - 싱글톤 UI 전용
    /// - LoadAssetAsync + Unity Instantiate 방식 (InstantiateAsync ❌)
    /// </summary>
    private async UniTask EnsureUIRootAsync()
    {
        if (!autoCreateUIRoot)
            return;

        if (_uiRootEnsured)
            return;

        // 1️⃣ 이미 존재하면 종료
        var existing = FindObjectOfType<UIRootBootstrapper>(true);
        if (existing != null)
        {
            _uiRootEnsured = true;
            return;
        }

        Debug.LogWarning("[Managers] UIRoot not found. Creating from Addressables...");

        // 2️⃣ 프리팹 로드 (Asset 캐시)
        GameObject uiRootPrefab;
        try
        {
            uiRootPrefab = await AddressableManager
                .LoadAssetAsync<GameObject>(uiRootPrefabKey);
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[Managers] Failed to load UIRoot prefab. key={uiRootPrefabKey}\n{e}");
            return;
        }

        if (uiRootPrefab == null)
        {
            Debug.LogError($"[Managers] UIRoot prefab is null. key={uiRootPrefabKey}");
            return;
        }

        // 3️⃣ Unity Instantiate (싱글톤이므로 Addressables Instantiate ❌)
        var go = Instantiate(uiRootPrefab);
        go.name = "@UIRoot";
        DontDestroyOnLoad(go);

        _uiRootEnsured = true;

        Debug.Log("[Managers] UIRoot created successfully");
    }

    private void OnApplicationQuit()
    {
        SaveGameData();
    }
    #endregion
    private async UniTask InitializeAddressablesAsync()
    {
        try
        {
            Debug.Log("Addressables 초기화 시작");
            await _addressableManager.InitAsync();
            Debug.Log("Addressables 초기화 완료");
        }
        catch (Exception e)
        {
            Debug.LogError($"Addressables 초기화 실패: {e.Message}");
        }
    }

    private async UniTask InitializeAnimationsAsync()
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

        await AnimationResources.PreloadClipsAsync(animKeys);
        Debug.Log("[Managers] AnimationResourceManager 초기화 완료");
    }

   
    #region Save / Clear
    private void SaveGameData()
    {
        // TODO: Stage, Player, Inventory, CharacterData 등 Run 단위 데이터 저장
    }

    public static void Clear()
    {
        if (s_instance != null)
        {
            s_instance._input?.Clear();
            s_instance._resource?.Clear();
            s_instance = null;
        }
    }
    #endregion

}