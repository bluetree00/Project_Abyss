
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
    private StageManager _stageManager;
    private UIManager _ui;


    public static InputManager Input => Instance._input ?? (Instance._input = new InputManager());
    public static ResourceManager Resource => Instance._resource ?? (Instance._resource = new ResourceManager());
    public static ObjectPoolerManager ObjectPooler => Instance._objectPoolerManager;
    public static StageManager Stage => Instance._stageManager;
    public static UIManager UI => Instance._ui ?? (Instance._ui = new UIManager());

    #endregion


    void Awake()
    {
        if (s_instance == null)
        {
            s_instance = this;
            DontDestroyOnLoad(this);

            // ObjectPoolerManager 초기화 코드 분리
            List<ObjectPoolerManager.Pool> initialPools = ObjectPoolEffectInitializer.GetInitialPools("BaseTest");
            _objectPoolerManager = new ObjectPoolerManager(initialPools.ToArray()); // 현재 리스트이고 생성자 형태가 배열임으로 여기서 배열로 변환후 생성자 타입에 넣어줌
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
            s_instance._input?.Clear(); // Input 매니저의 Clear() 호출
            s_instance._resource?.Clear(); // ResourceManager에 Clear() 메서드를 추가하여 리소스 정리
            s_instance = null;
        }
    }
}
