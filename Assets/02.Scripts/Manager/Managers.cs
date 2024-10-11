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
                Init(); // 인스턴스가 null일 때만 초기화
            }
            return s_instance;
        }
    }

    #region Core // 게임 코어 매니저
    private InputManager _input = new InputManager();
    private ResourceManager _resource = new ResourceManager();

    public static InputManager Input { get { return Instance._input; } }
    public static ResourceManager Resource { get { return Instance._resource; } }
    #endregion

    void Awake()
    {
        Init(); // Awake에서 초기화
    }

    void Update()
    {
        _input.OnUpdate();
    }

    private static void Init()
    {
        if (s_instance == null)
        {
            GameObject go = GameObject.Find("@Managers");
            if (go == null)
            {
                go = new GameObject { name = "@Managers" };
                go.AddComponent<Managers>();
            }
            if (!go.activeSelf)
                go.SetActive(true);

            DontDestroyOnLoad(go);
            s_instance = go.GetComponent<Managers>();
        }
    }

    public static void Clear()
    {
        if (s_instance != null)
        {
            s_instance._input.Clear(); // Input 매니저의 Clear() 호출
            s_instance = null; // 인스턴스 초기화
        }
    }
}
