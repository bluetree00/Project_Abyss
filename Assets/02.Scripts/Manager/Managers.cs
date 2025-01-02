
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
    public StageManager _stageManager; // StageManager 변수 선언
    private UIManager _ui;
    private StageTransitionManager _stageTransitionManager;
    private CharacterDataManager _characterDataManager;     //캐릭터 데이터 관리 매니저
    private WeaponManager weaponManager; // 무기 데이터 관리 매니저
    SceneManagerEx _scene = new SceneManagerEx();
    DataManager _data = new DataManager();

    public static InputManager Input => Instance._input ?? (Instance._input = new InputManager());
    public static ResourceManager Resource => Instance._resource ?? (Instance._resource = new ResourceManager());
    public static ObjectPoolerManager ObjectPooler => Instance._objectPoolerManager;
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

            // ObjectPoolerManager 초기화
            List<ObjectPoolerManager.Pool> initialPools = ObjectPoolEffectInitializer.GetInitialPools("BaseTest");
            _objectPoolerManager = new ObjectPoolerManager(initialPools.ToArray());

            
            if (_stageTransitionManager == null)
            {
                _stageTransitionManager = new StageTransitionManager();
            }

            //_stageTransitionManager.LoadChapter("Chapter1");
        }
        else
        {
            Destroy(gameObject);
        }
    }

    void Start() {
    
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


    // 매니저에서 처리해줘야할 작업 : MonoBehaviour가 필요한 작업들
    #region MonoBehaviour 필요한 작업


    void CheckWeapon()
    {
        
    }

    void OnApplicationQuit()
    {
        // 게임 실행 종료 시 초기값으로 복원
        //CharacterData.characterData.RestoreInitialStats();
        // 게임 종료 시 사용한 후 필요없는 로드파일들 메모리 해제
        Resources.UnloadUnusedAssets();
    }


    #endregion

}
