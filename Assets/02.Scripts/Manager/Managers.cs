
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


    public static InputManager Input => Instance._input ?? (Instance._input = new InputManager());
    public static ResourceManager Resource => Instance._resource ?? (Instance._resource = new ResourceManager());
    public static ObjectPoolerManager ObjectPooler => Instance._objectPoolerManager;
    public static StageManager Stage => Instance._stageManager; // StageManager 인스턴스를 반환
    public static UIManager UI => Instance._ui ?? (Instance._ui = new UIManager());
    public static StageTransitionManager StageTransitionManager => Instance._stageTransitionManager; // StageTransitionManager 인스턴스를 반환
    public static CharacterDataManager CharacterData => 
    Instance._characterDataManager ?? (Instance._characterDataManager = new CharacterDataManager());    //캐릭터 데이터 관리 매니저

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
        FindandDataSync(); // 게임 시작 후 오브젝트 로딩 후에 캐릭터 찾기
        CharacterData.characterData.Initialize(); //게임 시작 시 캐릭터 기본 스탯 초기값 저장
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


    void FindandDataSync()      //게임이 시작된 후 캐릭터를 찾아 캐릭터 데이터를 동기화시키는 작업
    {
        GameObject playerObj = GameObject.FindGameObjectWithTag("Player");
        if (playerObj != null){
            string characterName = playerObj.name;
            CharacterData currentPlayerData = Resource.Load<CharacterData>($"{characterName}");
            Debug.Log($"{currentPlayerData.name}");
            CharacterData.characterData = currentPlayerData; 
            Debug.Log($"<color=green>{CharacterData.characterData} 데이터 전달 완료 </color>");
        }
        else
        {   
            Debug.LogWarning($"<color=orange>{_characterDataManager.characterData} 데이터 없음 </color>");
        }
    }

    void CheckWeapon()
    {
        
    }

    void OnApplicationQuit()
    {
        // 게임 실행 종료 시 초기값으로 복원
        CharacterData.characterData.RestoreInitialStats();
        // 게임 종료 시 사용한 후 필요없는 로드파일들 메모리 해제
        Resources.UnloadUnusedAssets();
    }



    #endregion
    
}
