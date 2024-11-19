
using System.Collections;
using System.Collections.Generic;
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
    public StageManager _stageManager; // StageManager 변수 선언
    private UIManager _ui;
    private StageTransitionManager _stageTransitionManager;

    public static InputManager Input => Instance._input ?? (Instance._input = new InputManager());
    public static ResourceManager Resource => Instance._resource ?? (Instance._resource = new ResourceManager());
    public static ObjectPoolerManager ObjectPooler => Instance._objectPoolerManager;
    public static StageManager Stage => Instance._stageManager; // StageManager 인스턴스를 반환
    public static UIManager UI => Instance._ui ?? (Instance._ui = new UIManager());
    public static StageTransitionManager StageTransitionManager => Instance._stageTransitionManager; // StageTransitionManager 인스턴스를 반환

    #endregion

    void Awake()
    {
        if (s_instance == null)
        {
            s_instance = this;
            DontDestroyOnLoad(this);

            // ObjectPoolerManager 초기화
            List<ObjectPoolerManager.Pool> initialPools = ObjectPoolEffectInitializer.GetInitialPools("BaseTest");
            _objectPoolerManager = new ObjectPoolerManager(initialPools.ToArray());

            // // 스테이지 데이터를 초기화
            // List<StageManager.Stage> stages;
            // List<StageManager.ConnectionRestriction> restrictions;
            // string bossStageName;

            // stages = StageEffectInitializer.GetInitialStagesForChapter("Chapter1", out restrictions, out bossStageName);

            // // StageManager 인스턴스를 생성하고 _stageManager에 할당
            // _stageManager = new StageManager(stages, restrictions, bossStageName);
            
            // // 첫 번째 스테이지로 이동
            // _stageManager.MoveToNextStage(0);
            // StageTransitionManager 초기화
            if (_stageTransitionManager == null)
            {
                _stageTransitionManager = new StageTransitionManager();
            }

            _stageTransitionManager.LoadChapter("Chapter1");
        }
        else
        {
            Destroy(gameObject);
        }
    }

    void Update()
    {
        _input?.OnUpdate();
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
}
