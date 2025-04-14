using UnityEngine;
using BackEnd;

public class BackendManager : MonoBehaviour
{
    private void Awake()
    {
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
