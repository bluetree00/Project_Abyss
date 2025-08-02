using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using Cysharp.Threading.Tasks;

[Serializable]
public class LoadedAsset
{
    public string key;
    public AsyncOperationHandle handle;
}

public class AddressableManager
{
    private Dictionary<string, AsyncOperationHandle> loadedAssets = new Dictionary<string, AsyncOperationHandle>();
    public List<LoadedAsset> loadedAssetsList = new List<LoadedAsset>();

    // Task -> UniTask로 변경
    private UniTask? _initTask;
    private bool _isInitialized = false;
    private bool _initFailed = false;

    // Task -> UniTask로 변경
    public async UniTask InitAsync()
    {
        if (_isInitialized) return;

        // 초기화가 이미 진행 중이면 기존 작업 대기
        if (_initTask.HasValue)
        {
            await _initTask.Value;
            return;
        }

        try
        {
            _initTask = InitializeAddressablesAsync();
            await _initTask.Value;
            _isInitialized = true;
            _initFailed = false;
            Debug.Log("Addressables 초기화 성공");
        }
        catch (Exception ex)
        {
            _initFailed = true;
            Debug.LogError($"Addressables 초기화 실패: {ex}");
            throw;
        }
    }

    // Task -> UniTask로 변경
    private async UniTask InitializeAddressablesAsync()
    {
        try
        {
            var operation = Addressables.InitializeAsync();
            // Task -> UniTask로 변경
            await operation.ToUniTask();
        }
        catch (Exception ex)
        {
            Debug.LogError($"Addressables 초기화 중 오류: {ex.Message}");
            throw;
        }
    }

    public bool IsInitialized => _isInitialized;
    
    // Task -> UniTask로 변경
    private async UniTask EnsureInitializedAsync()
    {
        if (_isInitialized) return;

        if (!_initTask.HasValue)
        {
            await InitAsync();
        }
        else
        {
            await _initTask.Value;
        }

        if (_initFailed)
            throw new Exception("Addressables 초기화 실패");
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
    /// 어드레서블 시스템으로 데이터를 비동기적으로 로드하는 함수
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <param name="key"></param>
    /// <returns></returns>
    // Task -> UniTask로 변경
    public async UniTask<T> LoadAssetAsyncTask<T>(string key) where T : UnityEngine.Object
    {
        var handle = Addressables.LoadAssetAsync<T>(key);
        // Task -> UniTask로 변경
        await handle.ToUniTask();
        if (handle.Status == AsyncOperationStatus.Succeeded)
        {
            loadedAssets[key] = handle;
            UpdateLoadedAssetsList();
            return handle.Result;
        }
        else
        {
            Debug.LogError($"Failed to load asset: {key}");
            return null;
        }
    }

    /// <summary>
    /// 어드레서블 시스템으로 프리팹을 생성하는 함수
    /// </summary>
    /// <param name="key"></param>
    /// <param name="onSuccess"></param>
    /// <param name="onFailure"></param>
    // Task -> UniTask로 변경
    public async UniTask InstantiateAsync(string key, Action<GameObject> onSuccess, Action onFailure = null)
    {
        await EnsureInitializedAsync();
        Addressables.InstantiateAsync(key).Completed += handle =>
        {
            HandleCompletion(handle, key, onSuccess, onFailure);
        };
    }

    /// <summary>
    /// 어드레서블 시스템으로 프리팹을 생성하는 비동기 함수
    /// </summary>
    /// <param name="key"></param>
    // Task -> UniTask로 변경
    public async UniTask<GameObject> InstantiateAsyncTask(string key)
    {
        await EnsureInitializedAsync();
        try
        {
            var handle = Addressables.InstantiateAsync(key);
            // Task -> UniTask로 변경
            await handle.ToUniTask();
            if (handle.Status == AsyncOperationStatus.Succeeded)
                return handle.Result;
            Debug.LogError($"Failed to instantiate: {key}");
            return null;
        }
        catch (Exception ex)
        {
            Debug.LogError($"Exception during Addressables instantiate: {key} - {ex}");
            return null;
        }
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