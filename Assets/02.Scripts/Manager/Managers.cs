using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.AddressableAssets;

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
                DontDestroyOnLoad(go);
            }
            return s_instance;
        }
    }

    #region Core // 게임 코어 매니저
    private InputManager _input;
    private ResourceManager _resource;
    private ObjectPoolerManager _objectPoolerManager;

    public StageManager _stageManager; // StageManager 변수 선언
    private UIManager _ui;
    private CharacterDataManager _characterDataManager;     // 캐릭터 데이터 관리 매니저
    private GameEventManager gameEventManager; // 게임 이벤트 매니저
    SceneManagerEx _scene = new SceneManagerEx();
    DataManager _data = new DataManager();

    public static InputManager Input_M => Instance._input ?? (Instance._input = new InputManager());
    public static ResourceManager Resource => Instance._resource ?? (Instance._resource = new ResourceManager());
    public static ObjectPoolerManager ObjectPooler => Instance._objectPoolerManager;
    public static StageManager Stage => Instance._stageManager; // StageManager 인스턴스를 반환
    public static UIManager UI => Instance._ui ?? (Instance._ui = new UIManager());
    public static CharacterDataManager CharacterData => Instance._characterDataManager ?? (Instance._characterDataManager = new CharacterDataManager());
    public static GameEventManager GameEvent => Instance.gameEventManager ?? (Instance.gameEventManager = new GameEventManager());

    public static SceneManagerEx Scene { get { return Instance._scene; } }
    public static DataManager Data { get { return Instance._data; } }
    #endregion

    #region Data // 데이터 매니저
    public static GraphData _graphData { get; private set; }
    public static StageData _stageData { get; private set; } // StageData 인스턴스
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
    }

    async void Start()
    {
        //TODO : 어드레서블 키 값으로 자동으로 받을 수 있도록 수정 요망
        //FIXME : 추후 어드레서블 키 값을 input 형태로 받아올 수 있도록 수정 필요
        // 1. Addressable로 StageData 로드
        _stageData = await AddressableManager.Instance.LoadAssetAsyncTask<StageData>("Data/Chapter1");
        if (_stageData == null) { Debug.LogError("StageData 로드 실패!"); return; }

        // 2. GraphData에 그래프 생성 및 저장
        _graphData = await AddressableManager.Instance.LoadAssetAsyncTask<GraphData>("NewGraphData");
        if (_graphData == null){ Debug.LogError("GraphData 로드 실패!"); return; }
        _graphData.graph = MapGeneratorManager.MapGeneratorManager.Generate(_stageData.chapters[0]);

        // 3. StageManager 초기화 (매개변수 없이)
        _stageManager = new StageManager();
        _stageManager.Initialize();
    }

    public StageData GetStageData()
    {
        return _stageData;
    }
    public GraphData GetGraphData()
    {
        return _graphData;
    }
    

    // 예시로 다른 풀도 추가 외부에서는 Managers를 붙여서 접근 초기화
    // StartCoroutine(InitializeObjectPool("BaseTest"));
    // 비동기 방식으로 풀 데이터를 로드하여 풀러 초기화 진행 준비된 SO에 넣고 해당 이름을 매개변수로 전달 전달 방식은 enum의 내용을 사용 추후 DB도 사용가능
    public IEnumerator InitializeObjectPool(string effectPoolDataName) //이펙트 SO패기지 초기화 방식
    {

        bool isCompleted = false;
        List<ObjectPoolerManager.Pool> initialPools = null;

        // AddressablesManager를 통해 풀 데이터를 비동기 로드 (GetInitialPools는 콜백 방식으로 수정됨)
        ObjectPoolEffectInitializer.GetInitialPools(effectPoolDataName, pools =>
        {
            initialPools = pools;
            isCompleted = true;
        });

        // 풀 데이터 로드 완료까지 대기
        yield return new WaitUntil(() => isCompleted);

        // 로드된 풀 데이터를 이용하여 ObjectPoolerManager 초기화
        _objectPoolerManager = new ObjectPoolerManager(initialPools.ToArray());
        Debug.Log($"{effectPoolDataName} 풀 초기화 완료 (Addressables 방식)");
    }




    void Update()
    {
        _input?.OnUpdate();

        if (Input.GetKeyDown(KeyCode.Tab))
        {
            AddressableManager.Instance.InstantiateAsync("Character_01", (GameObject obj) =>
            {
                Debug.Log($"{obj.name} 생성 완료");
            },
            () =>
            {
                Debug.Log("<color=red>생성 실패</color>");
            });
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
