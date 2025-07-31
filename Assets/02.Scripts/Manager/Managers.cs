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
    public static GraphData _graphData { get; set; }
    private GraphData _loadedGraphData; // 로드된 GraphData 인스턴스
    public static StageData _stageData { get; private set; } // StageData 인스턴스
    private const string StageDataFile = "StageData.json";
    private const string GraphDataFile = "GraphData.json";
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
    }

    void Start()
    {
        StartCoroutine(InitializeManagersCoroutine());
        //TODO : 어드레서블 키 값으로 자동으로 받을 수 있도록 수정 요망
        //FIXME : 추후 어드레서블 키 값을 input 형태로 받아올 수 있도록 수정 필요

        // 1. StageData 로드 (없으면 새로 생성)
        // _stageData = DataManager.LoadJsonFile<StageData>(StageDataFile);
        // if (_stageData == null)
        // {
        //     Debug.LogWarning("StageData 파일이 없어 새로 생성합니다.");
        //     _stageData = ScriptableObject.CreateInstance<StageData>();
        //     _stageData.SetDefaultValues(); // 기본값 설정 메서드 호출

        //     SaveStageData();
        // }

        // // 2. GraphData 로드 (없으면 새로 생성)
        // _graphData = DataManager.LoadJsonFile<GraphData>(GraphDataFile);
        // if (_graphData == null)
        // {
        //     Debug.LogWarning("GraphData 파일이 없어 새로 생성합니다.");
        //     _graphData = ScriptableObject.CreateInstance<GraphData>();
        //     // 필요시 기본값 설정
        //     SaveGraphData();
        // }
        // _graphData.graph = MapGeneratorManager.MapGeneratorManager.Generate(_stageData.chapters[0]);


    }




    public StageData GetStageData()
    {
        return _stageData;
    }
    public GraphData GetGraphData()
    {
        return _graphData;
    }

    public void SaveStageData()
    {
        DataManager.SaveJsonFile(StageDataFile, _stageData);
    }

    public void LoadStageData()
    {
        _stageData = DataManager.LoadJsonFile<StageData>(StageDataFile);
        if (_stageData == null)
        {
            Debug.LogWarning("StageData 파일이 없어 새로 생성합니다.");
            _stageData = ScriptableObject.CreateInstance<StageData>();
            _stageData.SetDefaultValues();
            SaveStageData();
        }
    }

    public void SaveGraphData()
    {
        DataManager.SaveJsonFile(GraphDataFile, _graphData);
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
            StartStageasync();
            Debug.Log("StageManager StartStageasync 실행");
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


}