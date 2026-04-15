using UnityEngine;
using BackEnd;

public class BackendManager : MonoBehaviour
{
    private void Awake()
    {
        // AppBootstrapper가 이미 초기화한 경우 중복 실행 방지
        if (AppBootstrapper.IsBackendInitialized)
        {
            Destroy(gameObject);
            return;
        }

        DontDestroyOnLoad(gameObject);
        BackendSetup();
    }

    private void BackendSetup()
    {
        var bro = Backend.Initialize();

        if (bro.IsSuccess())
        {
            Debug.Log("Backend Initialization Success: " + bro.GetMessage());
        }
        else
        {
            Debug.LogError("Backend Initialization Failed: " + bro.GetMessage());
        }
    }
}
