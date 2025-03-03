using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

[Serializable]
public class LoadedAsset
{
    public string key;
    public AsyncOperationHandle handle;
}

public class AddressablesManager : MonoBehaviour
{
    private Dictionary<string, AsyncOperationHandle> loadedAssets = new Dictionary<string, AsyncOperationHandle>();
    [SerializeField]
    private List<LoadedAsset> loadedAssetsList = new List<LoadedAsset>();
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

    private void UpdateLoadedAssetsList()
    {
        loadedAssetsList.Clear();
        foreach (var kvp in loadedAssets)
        {
            loadedAssetsList.Add(new LoadedAsset { key = kvp.Key, handle = kvp.Value });
        }
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
     
     //동기 버전으로 사용시
    public T LoadAssetSync<T>(string key) where T : UnityEngine.Object
    {
        var handle = Addressables.LoadAssetAsync<T>(key);
        handle.WaitForCompletion();  // 동기적으로 대기
        return handle.Result;
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
            loadedAssets[key] = handle;
            UpdateLoadedAssetsList();
            onSuccess?.Invoke(handle.Result);
        }
        else
        {
            onFailure?.Invoke();
        }
    }

    /// <summary>
    /// 어드레서블 시스템으로 로드했던 리소스를 해제하는 함수
    /// </summary>
    /// <param name="key">해제할 리소스의 키</param>
    public void ReleaseAsset(string key)
    {
        if (loadedAssets.TryGetValue(key, out AsyncOperationHandle handle))
        {
            Addressables.Release(handle);
            Debug.Log($"<color=orange>리소스 데이터 : {key} 를 해제 </color>");
            loadedAssets.Remove(key);
            UpdateLoadedAssetsList();
        }
        else{
            Debug.LogError("해당 키의 리소스가 없습니다.");
        }
    }

    /// <summary>
    /// 인스턴스화하여 생성시켰던 오브젝트를 삭제하는 함수
    /// </summary>
    /// <param name="key">삭제할 오브젝트의 키</param>
    public void ReleaseInstance(string key)
    {
        if (loadedAssets.TryGetValue(key, out AsyncOperationHandle handle))
        {
            Addressables.ReleaseInstance(handle);
            Debug.Log($"<color=orange>오브젝트 : {key} 를 삭제 </color>");
            loadedAssets.Remove(key);
            UpdateLoadedAssetsList();
        }
        else{
            Debug.LogError("해당 키의 오브젝트가 없습니다.");
        }
    }
}