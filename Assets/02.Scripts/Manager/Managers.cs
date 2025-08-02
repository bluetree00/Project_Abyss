using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.AddressableAssets;
using Cysharp.Threading.Tasks;

public class Managers : MonoBehaviour
{
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
    [SerializeField]
    private List<LoadedAsset> _loadedAssetsList = new List<LoadedAsset>();

    #region Data // 데이터 매니저
    private const string StageDataFile = "StageData.json";
    private const string GraphDataFile = "GraphData.json";
    private StageGraphDataManager _stageGraphDataManager;
    public static StageGraphDataManager StageGraphData => Instance._stageGraphDataManager ??= new StageGraphDataManager();
    public static AddressableManager AddressableManager => Instance._addressableManager ?? (Instance._addressableManager = new AddressableManager());
    #endregion

    #region Core // 게임 코어 매니저
    private InputManager _input;
    private ResourceManager _resource;
    private ObjectPoolerManager _objectPoolerManager;
    private AddressableManager _addressableManager; // AddressableManager 인스턴스

    public StageManager _stageManager; // StageManager 변수 선언
    private UIManager _ui;
    private CharacterDataManager _characterDataManager;     // 캐릭터 데이터 관리 매니저
    private GameEventManager gameEventManager; // 게임 이벤트 매니저

    private PlayerManager _playerManager = new PlayerManager();
    SceneManagerEx _scene = new SceneManagerEx();
    DataManager _data = new DataManager();

    private MonsterDataManager _monsterDataManager;
    public static MonsterDataManager MonsterData => Instance._monsterDataManager ??= new MonsterDataManager();




    public static InputManager Input_M => Instance._input ?? (Instance._input = new InputManager());
    public static ResourceManager Resource => Instance._resource ?? (Instance._resource = new ResourceManager());
    public static ObjectPoolerManager ObjectPooler => Instance._objectPoolerManager;

    public static StageManager Stage => Instance._stageManager; // StageManager 인스턴스를 반환
    public static UIManager UI => Instance._ui ?? (Instance._ui = new UIManager());
    public static CharacterDataManager CharacterData => Instance._characterDataManager ?? (Instance._characterDataManager = new CharacterDataManager());
    public static GameEventManager GameEvent => Instance.gameEventManager ?? (Instance.gameEventManager = new GameEventManager());

    public static SceneManagerEx Scene { get { return Instance._scene; } }
    public static DataManager Data { get { return Instance._data; } }

    public static PlayerManager Player => Instance._playerManager;
    #endregion



    void Awake()
    {
        if (s_instance == null)
        {
            s_instance = this;
            DontDestroyOnLoad(this);

        }
        else
        {
            Destroy(gameObject);
        }
        _addressableManager = new AddressableManager();
        InitializeAddressablesAsync().Forget();
    }

    void Start()
    {

    }


    // 예시로 다른 풀도 추가 외부에서는 Managers를 붙여서 접근 초기화 추후 UniTask로 변경 예정
    // StartCoroutine(InitializeObjectPool("BaseTest"));
    // 비동기 방식으로 풀 데이터를 로드하여 풀러 초기화 진행 준비된 SO에 넣고 해당 이름을 매개변수로 전달 전달 방식은 enum의 내용을 사용 추후 DB도 사용가능
    public async System.Threading.Tasks.Task InitializeObjectPoolAsync(string effectPoolDataName)
    {
        if (string.IsNullOrEmpty(effectPoolDataName))
        {
            Debug.LogError("InitializeObjectPoolAsync: effectPoolDataName이 null이거나 빈 문자열입니다.");
            return;
        }

        List<ObjectPoolerManager.Pool> initialPools = await ObjectPoolEffectInitializer.GetInitialPoolsAsync(effectPoolDataName);

        if (initialPools == null || initialPools.Count == 0)
        {
            Debug.LogError($"InitializeObjectPoolAsync: '{effectPoolDataName}'에 대한 풀 데이터가 비어 있습니다.");
            return;
        }

        _objectPoolerManager = new ObjectPoolerManager(initialPools.ToArray());

        Debug.Log($"[ObjectPoolManager] '{effectPoolDataName}' 풀 초기화 완료 (Task 기반 Addressables 방식)");
    }


    void Update()
    {
        _input?.OnUpdate();

        //NOTE: 잠시 주석 처리 (원래 캐릭터 스폰용 코드)
        // if (Input.GetKeyDown(KeyCode.Tab))
        // {
        //     AddressableManager.InstantiateAsync("Character_01", (GameObject obj) =>
        //     {
        //         Debug.Log($"{obj.name} 생성 완료");
        //     },
        //     () =>
        //     {
        //         Debug.Log("<color=red>생성 실패</color>");
        //     });
        // }

        if (Input.GetKeyDown(KeyCode.Escape))
        {
            // 이미 열려 있으면 무시
            if (UI.HasPopup<UI_Pause>()) return;

            // 게임 멈추고 일시정지 UI 띄움
            Time.timeScale = 0f;
            UI.ShowPopupUI<UI_Pause>();
        }

        if (Input.GetKeyDown(KeyCode.G))
        {
            // _stageManager = new StageManager();
            // _stageManager.InitializeAsync();
            InitializeStageManagerAsync().Forget();
        }
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

    
    // 게임 데이터 저장 메서드
    private void SaveGameData()
    {
        try
        {
            Debug.Log("게임 데이터 저장 시작...");

            // StageGraphData 저장
            if (_stageGraphDataManager != null && _stageGraphDataManager.IsInitialized)
            {
                _stageGraphDataManager.SaveToJson();
                Debug.Log("스테이지 그래프 데이터 저장 완료");
            }

            // 향후 추가될 다른 데이터 매니저들
            // CharacterData, InventoryData 등...

            Debug.Log("모든 게임 데이터 저장 완료");
        }
        catch (System.Exception e)
        {
            Debug.LogError($"게임 데이터 저장 실패: {e.Message}");
        }
    }

    private void OnApplicationQuit()
    {
        SaveGameData();
    }

    #region 초기화 메서드 관리
    // G 키 입력 시 호출되는 스테이지 매니저 초기화 메서드
    private async UniTask InitializeStageManagerAsync()
    {
        try
        {
            Debug.Log("G 키 입력: 스테이지 매니저 초기화 시작");

            // AddressableManager 초기화 확인 (이미 초기화되어 있지 않다면 초기화 대기)
            if (_addressableManager == null || !_addressableManager.IsInitialized)
            {
                Debug.Log("AddressableManager 초기화 대기 중...");
                _addressableManager = _addressableManager ?? new AddressableManager();
                await _addressableManager.InitAsync();
                Debug.Log("AddressableManager 초기화 완료");
            }

            // 기존 스테이지 매니저가 있다면 정리
            if (_stageManager != null)
            {
                Debug.Log("기존 스테이지 매니저 정리 중...");
                try
                {
                    _stageManager.Cleanup();
                }
                catch (System.Exception cleanupEx)
                {
                    Debug.LogWarning($"기존 스테이지 매니저 정리 중 오류 (무시하고 계속): {cleanupEx.Message}");
                }
            }

            // 새 스테이지 매니저 생성 및 초기화
            _stageManager = new StageManager();
            await _stageManager.InitializeStageManager();

            Debug.Log("스테이지 매니저 초기화 완료!");
        }
        catch (System.Exception e)
        {
            Debug.LogError($"스테이지 매니저 초기화 실패: {e.Message}");
            Debug.LogError($"Stack Trace: {e.StackTrace}");
        }
    }

    // AddressableManager 초기화 메서드 추가
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
    #endregion


}