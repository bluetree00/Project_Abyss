using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using Cysharp.Threading.Tasks;

#region Debug Data

[Serializable]
public class LoadedAsset
{
    public string key;
    public AsyncOperationHandle handle;
}

#endregion

/// <summary>
/// Addressables를 안전하게 로드 / 생성 / 해제하는 로우 레벨 매니저
/// - key 기반 로딩
/// - handle 캐싱
/// - lifecycle 관리
/// ❌ 데이터 의미 / 포맷 / JSON 해석 책임 없음
/// </summary>
public class AddressableManager
{
    // -------------------------
    // State
    // -------------------------

    private bool _initialized;
    private UniTask? _initTask;

    // -------------------------
    // Loaded Handles
    // -------------------------

    private readonly Dictionary<string, AsyncOperationHandle> _handles
        = new Dictionary<string, AsyncOperationHandle>();

#if UNITY_EDITOR
    public IReadOnlyDictionary<string, AsyncOperationHandle> LoadedHandles => _handles;
    public List<LoadedAsset> loadedAssetsList = new List<LoadedAsset>();
#endif

    // -------------------------
    // Initialization
    // -------------------------

    public async UniTask InitAsync()
    {
        if (_initialized)
            return;

        if (_initTask.HasValue)
        {
            await _initTask.Value;
            return;
        }

        _initTask = InitializeInternalAsync();
        await _initTask.Value;
        _initialized = true;

        Debug.Log("[AddressableManager] Initialized");
    }

    private async UniTask InitializeInternalAsync()
    {
        var handle = Addressables.InitializeAsync();
        await handle.ToUniTask();

        if (handle.Status != AsyncOperationStatus.Succeeded)
            throw new Exception("Addressables initialization failed");
    }

    private async UniTask EnsureInitializedAsync()
    {
        if (!_initialized)
            await InitAsync();
    }

    // -------------------------
    // Public Load API
    // -------------------------

    public async UniTask<T> LoadAssetAsync<T>(string key)
        where T : UnityEngine.Object
    {
        await EnsureInitializedAsync();

        return await LoadInternalAsync(
            key,
            () => Addressables.LoadAssetAsync<T>(key)
        );
    }

    public async UniTask<GameObject> InstantiateAsync(string key)
    {
        await EnsureInitializedAsync();

        return await LoadInternalAsync(
            key,
            () => Addressables.InstantiateAsync(key)
        );
    }

    // -------------------------
    // Internal Unified Loader
    // -------------------------

    private async UniTask<T> LoadInternalAsync<T>(
        string key,
        Func<AsyncOperationHandle<T>> loader
    ) where T : UnityEngine.Object
    {
        if (_handles.TryGetValue(key, out var existing))
        {
            return (T)existing.Result;
        }

        var handle = loader();
        await handle.ToUniTask();

        if (handle.Status != AsyncOperationStatus.Succeeded)
        {
            Debug.LogError($"[AddressableManager] Load Failed: {key}");
            throw new Exception($"Failed to load Addressable: {key}");
        }

        _handles[key] = handle;
        UpdateDebugList();

        return handle.Result;
    }

    // -------------------------
    // Release
    // -------------------------

    public void Release(string key)
    {
        if (!_handles.TryGetValue(key, out var handle))
        {
            Debug.LogWarning($"[AddressableManager] Release failed (not found): {key}");
            return;
        }

        if (handle.Result is GameObject)
            Addressables.ReleaseInstance(handle);
        else
            Addressables.Release(handle);

        _handles.Remove(key);
        UpdateDebugList();

        Debug.Log($"[AddressableManager] Released: {key}");
    }

    public void ReleaseAll()
    {
        foreach (var kv in _handles)
        {
            var handle = kv.Value;

            if (handle.Result is GameObject)
                Addressables.ReleaseInstance(handle);
            else
                Addressables.Release(handle);
        }

        _handles.Clear();
        UpdateDebugList();

        Debug.Log("[AddressableManager] Released All");
    }

    // -------------------------
    // Debug
    // -------------------------

    private void UpdateDebugList()
    {
#if UNITY_EDITOR
        loadedAssetsList.Clear();
        foreach (var kv in _handles)
        {
            loadedAssetsList.Add(new LoadedAsset
            {
                key = kv.Key,
                handle = kv.Value
            });
        }
#endif
    }

    public static async UniTask<GameObject> LoadPrefabAsync(string key)
    {
        if (string.IsNullOrEmpty(key)) return null;

        AsyncOperationHandle<GameObject> handle = Addressables.LoadAssetAsync<GameObject>(key);
        await handle.ToUniTask(); // Task -> UniTask 변환

        if (handle.Status == AsyncOperationStatus.Succeeded)
            return handle.Result;

        Debug.LogError($"[AddressablesManager] Prefab '{key}' 로드 실패");
        return null;
    }


    public async UniTask PreloadAsync(IEnumerable<string> keys)
    {
        await EnsureInitializedAsync();

        foreach (var key in keys)
        {
            if (_handles.ContainsKey(key))
                continue;

            var handle = Addressables.LoadAssetAsync<UnityEngine.Object>(key);
            await handle.ToUniTask();

            if (handle.Status == AsyncOperationStatus.Succeeded)
                _handles[key] = handle;
        }
    }



}
