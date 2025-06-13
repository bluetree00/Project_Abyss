using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.VisualScripting;

//============================================================
// Managers 클래스
// 게임 내 여러 매니저들을 싱글톤으로 통합 관리하는 중앙 허브 역할
// 각종 매니저 인스턴스의 생성, 접근, 생명주기 관리 담당
//============================================================
public class Managers : MonoBehaviour
{
    // 싱글톤 인스턴스
    private static Managers s_instance;
    public static Managers Instance
    {
        get
        {
            if (s_instance == null)
            {
                // 씬 내 '@Managers' 오브젝트 검색
                GameObject go = GameObject.Find("@Managers");

                // 없으면 새로 생성
                if (go == null)
                {
                    go = new GameObject("@Managers");
                    go.AddComponent<Managers>();
                }

                s_instance = go.GetComponent<Managers>();
                DontDestroyOnLoad(go); // 씬 전환시 파괴 방지
            }

            return s_instance;
        }
    }

    //================================================================
    // Core Managers (핵심 매니저 인스턴스)
    //================================================================
    private InputManager _input;
    private ResourceManager _resource;
    private ObjectPoolerManager _objectPoolerManager;
    private StageManager _stageManager;
    private UIManager _ui;
    private StageTransitionManager _stageTransitionManager;
    private CharacterDataManager _characterDataManager;
    private GameEventManager _gameEventManager;
    private PlayerManager _playerManager = new PlayerManager();

    // 씬 관리, 데이터 관리용 클래스 (즉시 생성)
    private SceneManagerEx _scene = new SceneManagerEx();
    private DataManager _data = new DataManager();

    //================================================================
    // Static Accessors (외부에서 간편히 접근 가능하도록 static 프로퍼티 제공)
    // Lazy 초기화 방식 적용 (null일 경우 새 인스턴스 생성)
    // ObjectPoolerManager, StageManager 등은 직접 할당받도록 설계됨
    //================================================================
    public static InputManager Input_M => Instance._input ??= new InputManager();
    public static ResourceManager Resource => Instance._resource ??= new ResourceManager();
    public static ObjectPoolerManager ObjectPooler => Instance._objectPoolerManager;
    public static StageManager Stage => Instance._stageManager;
    public static UIManager UI => Instance._ui ??= new UIManager();
    public static StageTransitionManager StageTransitionManager => Instance._stageTransitionManager;
    public static CharacterDataManager CharacterData => Instance._characterDataManager ??= new CharacterDataManager();
    public static GameEventManager GameEvent => Instance._gameEventManager ??= new GameEventManager();
    public static PlayerManager Player => Instance._playerManager;
    public static SceneManagerEx Scene => Instance._scene;
    public static DataManager Data => Instance._data;

    //================================================================
    // Unity 라이프사이클
    //================================================================
    private void Awake()
    {
        if (s_instance == null)
        {
            s_instance = this;
            DontDestroyOnLoad(this); // 씬 전환시 파괴 방지

            // StageTransitionManager 초기화 (없으면 새로 생성)
            if (_stageTransitionManager == null)
                _stageTransitionManager = new StageTransitionManager();
        }
        else
        {
            // 중복 인스턴스 발견시 삭제
            Destroy(gameObject);
        }
    }

    private void Start()
    {
        // 게임 초기 챕터 데이터를 이용해 스테이지 전환 매니저 초기화
        _stageTransitionManager.Init("Data/Chapter1");
    }

    private void Update()
    {
        // 입력 매니저 업데이트 호출 (입력 처리)
        _input?.OnUpdate();

        // 디버그용 키 입력 처리 예시
        if (Input.GetKeyDown(KeyCode.Tab))
        {
            // Addressables를 이용해 캐릭터 프리팹 비동기 생성
            AddressablesManager.Instance.InstantiateAsync(
                "Character_01",
                obj => Debug.Log($"{obj.name} 생성 완료"),
                () => Debug.Log("<color=red>생성 실패</color>")
            );
        }

        // 좌우 화살표 키로 스테이지 이동
        if (Input.GetKeyDown(KeyCode.LeftArrow))
        {
            Stage.MoveToNextStage(-1);
        }
        else if (Input.GetKeyDown(KeyCode.RightArrow))
        {
            Stage.MoveToNextStage(1);
        }
    }

    //================================================================
    // 매니저 리셋/초기화 함수
    //================================================================
    public static void Clear()
    {
        if (s_instance != null)
        {
            s_instance._input?.Clear();
            s_instance._resource?.Clear();
            s_instance = null;
        }
    }

    //================================================================
    // 오브젝트 풀 초기화 (코루틴)
    // effectPoolDataName을 통해 초기 풀 데이터를 받아서 ObjectPoolerManager를 생성
    //================================================================
    public IEnumerator InitializeObjectPool(string effectPoolDataName)
    {
        bool isCompleted = false;
        List<ObjectPoolerManager.Pool> initialPools = null;

        // 초기 풀 데이터 비동기 로드 요청 (콜백으로 결과 전달)
        ObjectPoolEffectInitializer.GetInitialPools(effectPoolDataName, pools =>
        {
            initialPools = pools;
            isCompleted = true;
        });

        // 초기화 완료될 때까지 대기
        yield return new WaitUntil(() => isCompleted);

        // 오브젝트 풀 매니저 생성 및 초기화
        _objectPoolerManager = new ObjectPoolerManager(initialPools.ToArray());
        Debug.Log($"{effectPoolDataName} 풀 초기화 완료 (Addressables 방식)");
    }
}
