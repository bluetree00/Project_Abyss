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

    private void OnApplicationQuit()
    {
        SaveGameData();
    }
    #endregion

    #region Initialization Methods
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
            _objectPoolerManager.RegisterPools(pools.ToArray());
        }

        Debug.Log($"[Managers] 장비 풀 초기화 완료: {package.name} ({pools.Count} pools)");
    }
    #endregion

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



    public class PoolManager : MonoBehaviour
    {
        private ObjectPoolerManager _objectPoolerManager;

        /// <summary>
        /// Addressables 또는 SO 기반 Pool 초기화
        /// </summary>
        public async UniTask InitializeObjectPoolsAsync(string addressableKey)
        {
            // PoolDataPackage 로드
            PoolDataPackage package = await Managers.AddressableManager.LoadAssetAsync<PoolDataPackage>(addressableKey);
            if (package == null || package.Pools.Count == 0)
            {
                Debug.LogError($"초기화할 풀 데이터가 없습니다! ({addressableKey})");
                return;
            }

            // PoolDataPackage → ObjectPoolerManager용 리스트 변환
            List<ObjectPoolerManager.Pool> pools = await ObjectPoolDataInitializer.GetPoolsAsync(addressableKey);

            // ObjectPoolerManager 생성
            _objectPoolerManager = new ObjectPoolerManager(pools.ToArray());
            Debug.Log($"[ObjectPoolerManager] 초기화 완료 ({pools.Count} pools)");
        }

        public ObjectPoolerManager GetObjectPooler() => _objectPoolerManager;
    }
}
