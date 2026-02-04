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
    private bool _initialized;
    private UniTask? _initTask;

    /// <summary>
    /// ✅ 타입 충돌 방지: "실제 로드 타입까지 포함한 캐시 키"를 사용
    /// - 같은 address key라도 타입이 다르면 다른 캐시로 취급
    /// </summary>
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
    // ✅ Key Convention Helpers
    // -------------------------

    /// <summary>캐시 키: "원본키|타입"으로 분리해서 충돌 방지</summary>
    private static string MakeCacheKey<T>(string key) => $"{key}|{typeof(T).FullName}";

    // -------------------------
    // ✅ Explicit Public API
    // -------------------------

    /// <summary>
    /// 가장 기본 로드: 지정한 key에서 지정한 타입을 로드
    /// </summary>
    public async UniTask<T> LoadAssetAsync<T>(string key) where T : UnityEngine.Object
    {
        await EnsureInitializedAsync();
        return await LoadInternalAsync<T>(key, () => Addressables.LoadAssetAsync<T>(key));
    }

    /// <summary>
    /// ✅ 프리팹 인스턴스 생성은 "Instantiate"로 명시 (프리팹 키 전용)
    /// </summary>
    public async UniTask<GameObject> InstantiateAsync(string prefabKey)
    {
        await EnsureInitializedAsync();

        // Instantiate는 "인스턴스 핸들"이므로 캐시를 따로 관리하는 게 안전하지만,
        // 네 기존 정책을 유지하되 캐시 키를 분리해둔다.
        return await LoadInternalAsync<GameObject>(
            prefabKey,
            () => Addressables.InstantiateAsync(prefabKey)
        );
    }

    /// <summary>
    /// ✅ 명시 로드: 캐릭터 데이터는 DATA_ 키로만 로드한다는 의도를 코드에 남김
    /// </summary>
    public async UniTask<CharacterData> LoadCharacterDataAsync(string dataKey)
    {
        if (string.IsNullOrEmpty(dataKey))
        {
            Debug.LogError("[AddressableManager] LoadCharacterDataAsync failed: dataKey is empty");
            return null;
        }

        // 필요하면 여기서 규칙 강제 가능:
        // if (!dataKey.StartsWith("DATA_")) Debug.LogWarning(...);

        return await LoadAssetAsync<CharacterData>(dataKey);
    }

    /// <summary>
    /// ✅ 명시 로드: 플레이어 프리팹은 PREFAB_ 키로만 로드한다는 의도를 코드에 남김
    /// </summary>
    public async UniTask<GameObject> InstantiatePlayerAsync(string prefabKey)
    {
        if (string.IsNullOrEmpty(prefabKey))
        {
            Debug.LogError("[AddressableManager] InstantiatePlayerAsync failed: prefabKey is empty");
            return null;
        }

        // 필요하면 규칙 강제 가능:
        // if (!prefabKey.StartsWith("PREFAB_")) Debug.LogWarning(...);

        return await InstantiateAsync(prefabKey);
    }

    // -------------------------
    // Internal Unified Loader
    // -------------------------

    private async UniTask<T> LoadInternalAsync<T>(
        string key,
        Func<AsyncOperationHandle<T>> loader
    ) where T : UnityEngine.Object
    {
        // ✅ 타입 포함 캐시 키로 충돌 방지
        string cacheKey = MakeCacheKey<T>(key);

        if (_handles.TryGetValue(cacheKey, out var existing))
        {
            // ✅ 캐시된 결과 타입 체크(안전)
            if (existing.Result is T cached)
                return cached;

            Debug.LogError($"[AddressableManager] Cached type mismatch. key={key}, expected={typeof(T).Name}, actual={existing.Result?.GetType().Name}");
            _handles.Remove(cacheKey);
            UpdateDebugList();
        }

        var handle = loader();
        await handle.ToUniTask();

        if (handle.Status != AsyncOperationStatus.Succeeded)
        {
            Debug.LogError($"[AddressableManager] Load Failed: key={key}, type={typeof(T).Name}");
            throw new Exception($"Failed to load Addressable: {key} ({typeof(T).Name})");
        }

        // ✅ 로드 성공했지만 실제 타입이 다르면 여기서 명확히 에러
        UnityEngine.Object resultObj = handle.Result;
        if (resultObj != null && resultObj is not T)
        {
            Debug.LogError($"[AddressableManager] Type mismatch. key={key}, expected={typeof(T).Name}, actual={resultObj.GetType().Name}");
            throw new InvalidCastException($"Addressable type mismatch: key={key}, expected={typeof(T).Name}, actual={resultObj.GetType().Name}");
        }

        _handles[cacheKey] = handle;
        UpdateDebugList();
        return handle.Result;
    }

    // -------------------------
    // Release
    // -------------------------

    public void Release<T>(string key) where T : UnityEngine.Object
    {
        string cacheKey = MakeCacheKey<T>(key);

        if (!_handles.TryGetValue(cacheKey, out var handle))
        {
            Debug.LogWarning($"[AddressableManager] Release failed (not found): {cacheKey}");
            return;
        }

        if (handle.Result is GameObject)
            Addressables.ReleaseInstance(handle);
        else
            Addressables.Release(handle);

        _handles.Remove(cacheKey);
        UpdateDebugList();

        Debug.Log($"[AddressableManager] Released: {cacheKey}");
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
}
