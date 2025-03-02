using System;
using System.Collections;
using System.Collections.Generic;
using Unity.VisualScripting;
using UnityEngine;

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
    private EffectManager _effectManager;
    public StageManager _stageManager; // StageManager 변수 선언
    private UIManager _ui;
    private StageTransitionManager _stageTransitionManager;
    private CharacterDataManager _characterDataManager;     // 캐릭터 데이터 관리 매니저
    private WeaponManager weaponManager; // 무기 데이터 관리 매니저
    SceneManagerEx _scene = new SceneManagerEx();
    DataManager _data = new DataManager();

    public static InputManager Input_M => Instance._input ?? (Instance._input = new InputManager());
    public static ResourceManager Resource => Instance._resource ?? (Instance._resource = new ResourceManager());
    public static ObjectPoolerManager ObjectPooler => Instance._objectPoolerManager;
    public static EffectManager Effect => Instance._effectManager;
    public static StageManager Stage => Instance._stageManager; // StageManager 인스턴스를 반환
    public static UIManager UI => Instance._ui ?? (Instance._ui = new UIManager());
    public static StageTransitionManager StageTransitionManager => Instance._stageTransitionManager; // StageTransitionManager 인스턴스를 반환
    public static CharacterDataManager CharacterData => Instance._characterDataManager ?? (Instance._characterDataManager = new CharacterDataManager());
    public static WeaponManager Weapon => Instance.weaponManager ?? (Instance.weaponManager = new WeaponManager());

    public static SceneManagerEx Scene { get { return Instance._scene; } }
    public static DataManager Data { get { return Instance._data; } }
    #endregion

    void Awake()
    {
        if (s_instance == null)
        {
            s_instance = this;
            DontDestroyOnLoad(this);

            // 나머지 초기화 작업
            if (_stageTransitionManager == null)
                _stageTransitionManager = new StageTransitionManager();
        }
        else
        {
            Destroy(gameObject);
        }
    }

    void Start()
    {
        // 비동기 방식으로 풀 데이터를 로드하여 풀러 초기화 진행
        StartCoroutine(InitializeManagers());
    }

    private IEnumerator InitializeManagers()
    {
        bool isCompleted = false;
        List<ObjectPoolerManager.Pool> initialPools = null;
        
        // AddressablesManager를 통해 풀 데이터를 비동기 로드 (GetInitialPools는 콜백 방식으로 수정됨)
        ObjectPoolEffectInitializer.GetInitialPools("BaseTest", pools =>
        {
            initialPools = pools;
            isCompleted = true;
        });
        
        // 풀 데이터 로드 완료까지 대기
        yield return new WaitUntil(() => isCompleted);
        
        // 로드된 풀 데이터를 이용하여 ObjectPoolerManager 초기화
        _objectPoolerManager = new ObjectPoolerManager(initialPools.ToArray());
        Debug.Log("ObjectPoolerManager 초기화 완료 (Addressables 방식)");
    }

    void Update()
    {
        _input?.OnUpdate();

        if (Input.GetKeyDown(KeyCode.Tab))
        {
            AddressablesManager.Instance.InstantiateAsync("Character_01", (GameObject obj) =>
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

    public void ReloadStageManager(List<StageManager.Stage> stages, List<StageManager.ConnectionRestriction> restrictions, string bossStageName)
    {
        if (_stageManager != null)
        {
            _stageManager = new StageManager(stages, restrictions, bossStageName);
            _stageManager.MoveToNextStage(0); // 새로운 스테이지로 이동
        }
    }

    public void CreateNewObjectPooler(string newWeaponName)
    {
        // 새로운 무기가 추가될 때 비동기 방식으로 풀 데이터를 로드하여 새로운 풀러 생성
        ObjectPoolEffectInitializer.GetInitialPools(newWeaponName, pools =>
        {
            _objectPoolerManager = new ObjectPoolerManager(pools.ToArray());
            Debug.Log($"새로운 오브젝트 풀러가 생성되었습니다: {newWeaponName}");
        });
    }
}
