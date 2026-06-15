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

    /// <summary>
    /// PR6: 스코프(예: 챕터) 단위 해제용 — 스코프명 → 그 스코프로 로드된 cacheKey 집합.
    /// 스코프 없이 로드하면 영구 캐시(현행). 스코프 로드를 쓰는 코드가 없으면 ReleaseScope는 무동작.
    /// </summary>
    private readonly Dictionary<string, HashSet<string>> _scopeKeys = new();

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
    /// ✅ 안전 로드: 키가 Addressable 카탈로그에 없거나 로드 실패해도 예외를 던지지 않고 null을 반환.
    /// (Addressables는 존재하지 않는 키에 대해 InvalidKeyException을 LogException까지 해버리므로
    ///  위치 존재 여부를 LoadResourceLocationsAsync로 먼저 확인한다.)
    /// </summary>
    public async UniTask<T> TryLoadAssetAsync<T>(string key) where T : UnityEngine.Object
    {
        if (string.IsNullOrEmpty(key)) return null;

        await EnsureInitializedAsync();

        string cacheKey = MakeAssetCacheKey<T>(key);

        if (_assetHandles.TryGetValue(cacheKey, out var existing))
        {
            if (existing.IsValid() && existing.Result is T cached)
                return cached;

            _assetHandles.Remove(cacheKey);
            UpdateDebugList();
        }

        // 키 존재 사전 체크 — 없으면 빈 결과, 예외/로그 없음
        var locHandle = Addressables.LoadResourceLocationsAsync(key, typeof(T));
        await locHandle.ToUniTask();
        bool exists = locHandle.Status == AsyncOperationStatus.Succeeded
                      && locHandle.Result != null && locHandle.Result.Count > 0;
        Addressables.Release(locHandle);

        if (!exists) return null;

        var handle = Addressables.LoadAssetAsync<T>(key);
        await handle.ToUniTask();

        if (handle.Status != AsyncOperationStatus.Succeeded)
            return null;

        _assetHandles[cacheKey] = handle;
        UpdateDebugList();
        return handle.Result;
    }

    /// <summary>
    /// (선택) 명시 로드: 캐릭터 데이터
    /// </summary>
    public UniTask<CharacterData> LoadCharacterDataAsync(string dataKey)
        => LoadAssetAsync<CharacterData>(dataKey);

    /// <summary>
    /// PR6: 스코프 태그를 달아 로드한다. 같은 스코프로 로드된 에셋은 ReleaseScope(scope)로 일괄 해제 가능.
    /// scope가 null/빈 문자열이면 일반 LoadAssetAsync와 동일(영구 캐시).
    /// </summary>
    public async UniTask<T> LoadAssetAsync<T>(string key, string scope) where T : UnityEngine.Object
    {
        var result = await LoadAssetAsync<T>(key);
        if (!string.IsNullOrEmpty(scope) && result != null)
        {
            if (!_scopeKeys.TryGetValue(scope, out var set))
            {
                set = new HashSet<string>();
                _scopeKeys[scope] = set;
            }
            set.Add(MakeAssetCacheKey<T>(key));
        }
        return result;
    }

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
    }

    // -------------------------
    // Release Instance (InstantiateAsync로 만든 것만)
    // -------------------------

    /// <summary>
    /// ✅ 이 GameObject가 InstantiateAsync로 만들어 추적 중인 인스턴스인지 여부.
    /// - 비풀 Despawn 경계에서 ReleaseInstance vs Destroy 분기에 사용(경고 로그 없이 판별).
    /// </summary>
    public bool IsTrackedInstance(GameObject instance)
        => instance != null && _instanceHandles.ContainsKey(instance.GetInstanceID());

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

    /// <summary>
    /// PR6: 한 스코프로 로드된 에셋 핸들만 해제한다. 공용/영구 에셋(스코프 미지정)은 건드리지 않는다.
    /// ⚠ 호출 전 해당 에셋을 참조하는 풀/인스턴스가 모두 정리됐는지 보장해야 한다(분홍텍스처/NRE 방지).
    /// 스코프 로드를 쓰는 코드가 없으면 해제 대상이 없어 무동작.
    /// </summary>
    public void ReleaseScope(string scope)
    {
        if (string.IsNullOrEmpty(scope) || !_scopeKeys.TryGetValue(scope, out var keys)) return;

        foreach (var cacheKey in keys)
        {
            if (_assetHandles.TryGetValue(cacheKey, out var handle))
            {
                if (handle.IsValid()) Addressables.Release(handle);
                _assetHandles.Remove(cacheKey);
            }
        }

        _scopeKeys.Remove(scope);
        UpdateDebugList();
        Debug.Log($"[AddressableManager] Released Scope: {scope} ({keys.Count} assets)");
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
                type = h.IsValid() && h.Result != null ? h.Result.GetType().Name : "invalid",
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
                type = h.IsValid() && h.Result != null ? h.Result.GetType().Name : "invalid",
                isValid = h.IsValid()
            });
        }
#endif
    }
}
