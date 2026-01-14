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

    // public StageManager _stageManager;
    private UIManager _ui;
    private CharacterDataManager _characterDataManager;
    private GameEventManager gameEventManager;

    private PlayerManager _playerManager = new PlayerManager();
    private SceneManagerEx _scene = new SceneManagerEx();
    private DataManager _data = new DataManager();
    private MonsterDataManager _monsterDataManager;

    // Core manager static accessors
    public static InputManager Input => Instance._input ??= new InputManager();
    public static ResourceManager Resource => Instance._resource ??= new ResourceManager();
    public static ObjectPoolerManager ObjectPooler => Instance._objectPoolerManager;
    public static AddressableManager AddressableManager => Instance._addressableManager ??= new AddressableManager();
    public static AnimationResourceManager AnimationResources => Instance._animationResources ??= new AnimationResourceManager();
    // public static StageManager Stage => Instance._stageManager;
    public static UIManager UI => Instance._ui ??= new UIManager();
    public static CharacterDataManager CharacterData => Instance._characterDataManager ??= new CharacterDataManager();
    public static GameEventManager GameEvent => Instance.gameEventManager ??= new GameEventManager();
    public static PlayerManager Player => Instance._playerManager;
    public static SceneManagerEx Scene => Instance._scene;
    public static DataManager Data => Instance._data;
    public static MonsterDataManager MonsterData => Instance._monsterDataManager ??= new MonsterDataManager();
    #endregion

    #region Data Managers
    private const string StageDataFile = "StageData.json";
    private const string GraphDataFile = "GraphData.json";
    private StageGraphDataManager _stageGraphDataManager;
    public static StageGraphDataManager StageGraphData => Instance._stageGraphDataManager ??= new StageGraphDataManager();
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

        // Addressables 초기화 후 애니메이션 리소스 초기화
        InitializeAddressablesAsync().ContinueWith(async () =>
        {
            await InitializeAnimationsAsync();
        }).Forget();
    }

    void Update()
    {
        // 1. InputManager 업데이트 호출 (Action 기반 키/마우스 이벤트 처리)
        _input?.OnUpdate();

        // 2. 키 입력 처리 (GetKeyDown/Up)
        if (_input != null)
        {
            // ESC: 일시정지 UI
            if (_input.GetKeyDown(KeyCode.Escape))
            {
                if (!UI.HasPopup<UI_Pause>())
                {
                    Time.timeScale = 0f;
                    UI.ShowPopupUI<UI_Pause>();
                }
            }

            // // G: 스테이지 매니저 초기화
            // if (_input.GetKeyDown(KeyCode.G))
            // {
            //     InitializeStageManagerAsync().Forget();
            // }

        }

        // 3. 추가 Action 기반 이벤트 처리 (KeyAction / MouseAction 등)
        // 이미 OnUpdate()에서 처리됨
    }


    private void OnApplicationQuit()
    {
        SaveGameData();
    }
    #endregion

    #region Initialization Methods
    /// <summary>
    /// Addressables 초기화
    /// </summary>
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

    /// <summary>
    /// 애니메이션 리소스 미리 로드 //TODO: 추후 해당 내용은 게임이 시작될때 필요한 애니메이션 리소스를 로드하는 방식으로 이전
    /// </summary>
    private async UniTask InitializeAnimationsAsync()
    {
        // 예시 애니메이션 키 목록
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

    /// <summary>
    /// 스테이지 매니저 초기화
    /// </summary>
    // private async UniTask InitializeStageManagerAsync()
    // {
    //     try
    //     {
    //         Debug.Log("스테이지 매니저 초기화 시작");

    //         // Addressables 준비 확인
    //         if (_addressableManager == null || !_addressableManager.IsInitialized)
    //         {
    //             _addressableManager ??= new AddressableManager();
    //             await _addressableManager.InitAsync();
    //         }

    //         // 기존 스테이지 매니저 정리
    //         _stageManager?.Cleanup();

    //         // 새 스테이지 매니저 생성 및 초기화
    //         _stageManager = new StageManager();
    //         await _stageManager.InitializeStageManager();

    //         Debug.Log("스테이지 매니저 초기화 완료");
    //     }
    //     catch (Exception e)
    //     {
    //         Debug.LogError($"스테이지 매니저 초기화 실패: {e.Message}");
    //     }
    // }

    /// <summary>
    /// 오브젝트 풀 초기화 (Addressables 기반)
    /// </summary>
    public async Task InitializeObjectPoolAsync(string effectPoolDataName)
    {
        if (string.IsNullOrEmpty(effectPoolDataName))
        {
            Debug.LogError("effectPoolDataName이 null 또는 빈 문자열입니다.");
            return;
        }

        var initialPools = await ObjectPoolEffectInitializer.GetInitialPoolsAsync(effectPoolDataName);
        if (initialPools == null || initialPools.Count == 0)
        {
            Debug.LogError($"'{effectPoolDataName}' 풀 데이터가 비어 있습니다.");
            return;
        }

        _objectPoolerManager = new ObjectPoolerManager(initialPools.ToArray());
        Debug.Log($"[ObjectPoolManager] '{effectPoolDataName}' 초기화 완료");
    }

    // Managers에서 등록 하는 무기 이펙트 풀 초기화
        public async UniTask InitializeWeaponEffectPoolsAsync(WeaponEffectPackageSO package, int defaultPoolSize = 5)
    {
        if (package == null) return;

        var pools = await WeaponEffectPackagePoolInitializer.GetPoolsAsync(package, defaultPoolSize);
        if (pools.Count == 0) return;

        if (_objectPoolerManager == null)
        {
            _objectPoolerManager = new ObjectPoolerManager(pools.ToArray());
        }
        else
        {
            _objectPoolerManager.RegisterPools(pools.ToArray()); // <-- 기존 풀에 병합
        }

        Debug.Log($"[Managers] 장비 풀 초기화 완료: {package.name} ({pools.Count} pools)");
    }

    #endregion

    #region Save / Clear
    /// <summary>
    /// 게임 데이터 저장
    /// </summary>
    private void SaveGameData()
    {
        try
        {
            Debug.Log("게임 데이터 저장 시작...");

            if (_stageGraphDataManager != null && _stageGraphDataManager.IsInitialized)
            {
                _stageGraphDataManager.SaveToJson();
                Debug.Log("스테이지 그래프 데이터 저장 완료");
            }

            // 향후 CharacterData, InventoryData 등 추가 가능

            Debug.Log("모든 게임 데이터 저장 완료");
        }
        catch (Exception e)
        {
            Debug.LogError($"게임 데이터 저장 실패: {e.Message}");
        }
    }

    /// <summary>
    /// 
    /// 매니저 초기화 해제
    /// </summary>
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
