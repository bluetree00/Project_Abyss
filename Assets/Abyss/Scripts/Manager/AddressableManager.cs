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
    public string kind; // "Asset" or "Instance"
    public string type;
    public bool isValid;
}

#endregion

/// <summary>
/// Addressables를 안전하게 로드 / 인스턴스 생성 / 해제하는 로우 레벨 매니저
/// - Asset 로드(handle 캐싱): key + type 기반 캐시
/// - Instantiate(handle 추적): 인스턴스별 handle 저장 (캐시 ❌)
/// - Release: Asset은 Release(handle), Instance는 ReleaseInstance(handle)
/// ❌ 데이터 의미 / 포맷 / JSON 해석 책임 없음
/// </summary>
public sealed class AddressableManager
{
    private bool _initialized;
    private UniTask? _initTask;

    /// <summary>
    /// ✅ Asset Handle 캐시 (key|type)
    /// </summary>
    private readonly Dictionary<string, AsyncOperationHandle> _assetHandles = new();

    /// <summary>
    /// ✅ Instance Handle 추적 (instanceId -> handle)
    /// - Addressables.InstantiateAsync()로 만든 인스턴스만 등록됨
    /// - LoadAsset + Unity Instantiate 로 만든 인스턴스는 여기 등록되지 않음(일반 Destroy로 관리)
    /// </summary>
    private readonly Dictionary<int, AsyncOperationHandle<GameObject>> _instanceHandles = new();

#if UNITY_EDITOR
    public IReadOnlyDictionary<string, AsyncOperationHandle> LoadedAssetHandles => _assetHandles;
    public IReadOnlyDictionary<int, AsyncOperationHandle<GameObject>> LoadedInstanceHandles => _instanceHandles;
    public List<LoadedAsset> loadedAssetsList = new();
#endif

    // -------------------------
    // Initialization
    // -------------------------

    public async UniTask InitAsync()
    {
        if (_initialized) return;

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
            throw new Exception("[AddressableManager] Addressables initialization failed");
    }

    private async UniTask EnsureInitializedAsync()
    {
        if (!_initialized)
            await InitAsync();
    }

    // -------------------------
    // Key Helpers
    // -------------------------

    /// <summary>캐시 키: "원본키|타입"으로 충돌 방지</summary>
    private static string MakeAssetCacheKey<T>(string key) => $"{key}|{typeof(T).FullName}";

    // -------------------------
    // Asset Load (캐시 O)
    // -------------------------

    /// <summary>
    /// ✅ 가장 기본 로드: 지정한 key에서 지정한 타입을 로드 (캐시 O)
    /// </summary>
    public async UniTask<T> LoadAssetAsync<T>(string key) where T : UnityEngine.Object
    {
        if (string.IsNullOrEmpty(key))
            throw new ArgumentException("[AddressableManager] LoadAssetAsync failed: key is null/empty");

        await EnsureInitializedAsync();

        string cacheKey = MakeAssetCacheKey<T>(key);

        if (_assetHandles.TryGetValue(cacheKey, out var existing))
        {
            if (existing.IsValid() && existing.Result is T cached)
                return cached;

            // invalid or mismatch -> drop and reload
            _assetHandles.Remove(cacheKey);
            UpdateDebugList();
        }

        var handle = Addressables.LoadAssetAsync<T>(key);
        await handle.ToUniTask();

        if (handle.Status != AsyncOperationStatus.Succeeded)
            throw new Exception($"[AddressableManager] Load Failed: key={key}, type={typeof(T).Name}");

        _assetHandles[cacheKey] = handle;
        UpdateDebugList();
        return handle.Result;
    }

    /// <summary>
    /// (선택) 명시 로드: 캐릭터 데이터
    /// </summary>
    public UniTask<CharacterData> LoadCharacterDataAsync(string dataKey)
        => LoadAssetAsync<CharacterData>(dataKey);

    // -------------------------
    // Instantiate (캐시 X, 인스턴스 추적 O)
    // -------------------------

    /// <summary>
    /// ✅ Addressables.InstantiateAsync로 "새 인스턴스" 생성 (캐시 X)
    /// - 생성된 인스턴스는 instanceId로 handle을 추적하여 ReleaseInstance가 가능
    /// </summary>
    public async UniTask<GameObject> InstantiateAsync(string prefabKey, Transform parent = null, bool inWorldSpace = false)
    {
        if (string.IsNullOrEmpty(prefabKey))
            throw new ArgumentException("[AddressableManager] InstantiateAsync failed: prefabKey is null/empty");

        await EnsureInitializedAsync();

        AsyncOperationHandle<GameObject> handle =
            parent != null
                ? Addressables.InstantiateAsync(prefabKey, parent, inWorldSpace)
                : Addressables.InstantiateAsync(prefabKey);

        await handle.ToUniTask();

        if (handle.Status != AsyncOperationStatus.Succeeded || handle.Result == null)
            throw new Exception($"[AddressableManager] Instantiate Failed: key={prefabKey}");

        var go = handle.Result;
        int id = go.GetInstanceID();
        _instanceHandles[id] = handle;

        UpdateDebugList();
        return go;
    }

    /// <summary>
    /// (선택) 명시 인스턴스: 플레이어 프리팹
    /// </summary>
    public UniTask<GameObject> InstantiatePlayerAsync(string prefabKey, Transform parent = null)
        => InstantiateAsync(prefabKey, parent);

    // -------------------------
    // Release Asset (캐시 O 대상)
    // -------------------------

    public void ReleaseAsset<T>(string key) where T : UnityEngine.Object
    {
        string cacheKey = MakeAssetCacheKey<T>(key);

        if (!_assetHandles.TryGetValue(cacheKey, out var handle))
        {
            Debug.LogWarning($"[AddressableManager] ReleaseAsset failed (not found): {cacheKey}");
            return;
        }

        if (handle.IsValid())
            Addressables.Release(handle);

        _assetHandles.Remove(cacheKey);
        UpdateDebugList();

        Debug.Log($"[AddressableManager] Released Asset: {cacheKey}");
    }

    // -------------------------
    // Release Instance (InstantiateAsync로 만든 것만)
    // -------------------------

    /// <summary>
    /// ✅ InstantiateAsync로 생성된 인스턴스만 Addressables.ReleaseInstance가 가능
    /// - LoadAsset + Unity Instantiate로 만든 오브젝트는 여기로 해제하지 말고 Destroy로 처리
    /// </summary>
    public bool ReleaseInstance(GameObject instance)
    {
        if (instance == null) return false;

        int id = instance.GetInstanceID();

        if (!_instanceHandles.TryGetValue(id, out var handle))
        {
            Debug.LogWarning($"[AddressableManager] ReleaseInstance ignored (not tracked). name={instance.name}");
            return false;
        }

        if (handle.IsValid())
            Addressables.ReleaseInstance(handle);

        _instanceHandles.Remove(id);
        UpdateDebugList();

        return true;
    }

    // -------------------------
    // Release All
    // -------------------------

    public void ReleaseAllInstances()
    {
        foreach (var kv in _instanceHandles)
        {
            var handle = kv.Value;
            if (handle.IsValid())
                Addressables.ReleaseInstance(handle);
        }

        _instanceHandles.Clear();
        UpdateDebugList();

        Debug.Log("[AddressableManager] Released All Instances");
    }

    public void ReleaseAllAssets()
    {
        foreach (var kv in _assetHandles)
        {
            var handle = kv.Value;
            if (handle.IsValid())
                Addressables.Release(handle);
        }

        _assetHandles.Clear();
        UpdateDebugList();

        Debug.Log("[AddressableManager] Released All Assets");
    }

    public void ReleaseAll()
    {
        ReleaseAllInstances();
        ReleaseAllAssets();
    }

    // -------------------------
    // Debug
    // -------------------------

    private void UpdateDebugList()
    {
#if UNITY_EDITOR
        loadedAssetsList.Clear();

        foreach (var kv in _assetHandles)
        {
            var h = kv.Value;
            loadedAssetsList.Add(new LoadedAsset
            {
                key = kv.Key,
                kind = "Asset",
                type = h.Result != null ? h.Result.GetType().Name : "null",
                isValid = h.IsValid()
            });
        }

        foreach (var kv in _instanceHandles)
        {
            var h = kv.Value;
            loadedAssetsList.Add(new LoadedAsset
            {
                key = $"InstanceId:{kv.Key}",
                kind = "Instance",
                type = h.Result != null ? h.Result.GetType().Name : "null",
                isValid = h.IsValid()
            });
        }
#endif
    }
}
