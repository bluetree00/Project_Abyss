
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


    public static InputManager Input => Instance._input ?? (Instance._input = new InputManager());
    public static ResourceManager Resource => Instance._resource ?? (Instance._resource = new ResourceManager());
    public static ObjectPoolerManager ObjectPooler => Instance._objectPoolerManager;

    #endregion

    void Awake()
    {
        if (s_instance == null)
        {
            s_instance = this;
            DontDestroyOnLoad(this);

                // ObjectPoolerManager 초기화 추후 초기화 전용 스크립트에 분할
            ObjectPoolerManager.Pool[] pools = {
                new ObjectPoolerManager.Pool { tag = "ShinySlash", resourcePath = "Effects/ShinySlash", initialSize = 10 },
                new ObjectPoolerManager.Pool { tag = "HitEffect_02", resourcePath = "Effects/HitEffect_02", initialSize = 10 },
                new ObjectPoolerManager.Pool { tag = "DieEffect_01", resourcePath = "Effects/DieEffect_01", initialSize = 5 }
            };
            _objectPoolerManager = new ObjectPoolerManager(pools);
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
