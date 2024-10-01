using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Managers : MonoBehaviour
{
    static Managers s_instance; // 사용할 매니저
    public static Managers Instance { get { Init(); return s_instance; } } // 유일한 매니저를 가져옴

    #region Core // 게임 코어 매니저
    InputManager _input = new InputManager();
    ResourceManager _resource = new ResourceManager();
    public static InputManager Input { get { return Instance._input; } }
    public static ResourceManager Resource { get { return Instance._resource; } }
    #endregion

    void Start()
    {
        Init();
    }

    void Update()
    {
        _input.OnUpdate();
    }

    public static void Init()
    {
        if (s_instance == null)
        {
            GameObject go = GameObject.Find("@Managers");

            if (go == null)
            {
                // @Managers 오브젝트가 없으면 새로 생성
                go = new GameObject { name = "@Managers" };
                go.AddComponent<Managers>();
            }

            
            // 오브젝트가 비활성화된 경우 활성화
            if (!go.activeSelf)
            {
                go.SetActive(true);
            }
            

            DontDestroyOnLoad(go); // 씬이 변경되어도 유지
            s_instance = go.GetComponent<Managers>();
        }
    }

    public static void Clear()
    {
        s_instance = null;
        Input.Clear();
    }

    public void CoroutineHelper(IEnumerator coroutine)
    {
        StartCoroutine(coroutine);
    }
}
