using System;
using System.Collections;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

public class AddressablesManager : MonoBehaviour
{
    private static AddressablesManager _instance;
    public static AddressablesManager Instance
    {
        get
        {
            if (_instance == null)
            {
                GameObject go = new GameObject("AddressablesManager");
                _instance = go.AddComponent<AddressablesManager>();
                DontDestroyOnLoad(go);
            }
            return _instance;
        }
    }

    private void Start()
    {
        StartCoroutine(InitAddressables());
    }

    IEnumerator InitAddressables()
    {
        var Init = Addressables.InitializeAsync();
        yield return Init;
    }
    
    /// <summary>
    /// 어드레서블 시스템으로 데이터를 로드하는 함수
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <param name="key"></param>
    /// <param name="onSuccess"></param>
    /// <param name="onFailure"></param>
    public void LoadAsset<T>(string key, Action<T> onSuccess = null, Action onFailure = null) where T : UnityEngine.Object
    {
        Addressables.LoadAssetAsync<T>(key).Completed += handle =>
        {
            HandleCompletion(handle, key, onSuccess, onFailure);
        };
    }
    /// <summary>
    /// 어드레서블 시스템으로 프리팹을 생성하는 함수
    /// </summary>
    /// <param name="key"></param>
    /// <param name="onSuccess"></param>
    /// <param name="onFailure"></param>
    public void InstantiateAsync(string key, Action<GameObject> onSuccess, Action onFailure = null)
    {
        Addressables.InstantiateAsync(key).Completed += handle =>
        {
            HandleCompletion(handle, key, onSuccess, onFailure);
        };
    }

    private void HandleCompletion<T>(AsyncOperationHandle<T> handle, string key, Action<T> onSuccess, Action onFailure = null) where T : UnityEngine.Object
    {
        if (handle.Status == AsyncOperationStatus.Succeeded)
        {
            onSuccess?.Invoke(handle.Result);
        }
        else
        {
            onFailure?.Invoke();
        }
    }
}