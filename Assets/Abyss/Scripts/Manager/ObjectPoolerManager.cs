using System;
using System.Collections.Generic;
using UnityEngine;
using Cysharp.Threading.Tasks;

/// <summary>
/// 실무형 ObjectPooler
/// - Spawn 요청 시 Pool 없으면 Addressables 통해 자동 로드
/// - PoolType(카테고리) 기준으로 정리
/// - Addressables 직접 호출 ❌ (AddressableManager만 사용)
/// </summary>
public class ObjectPoolerManager
{
    // =========================
    // Types
    // =========================

    public enum PoolType
    {
        Effect,
        Monster,
        Character,
        UI
    }

    [Serializable]
    public class PoolConfig
    {
        public string key;                 // Addressables key == pool tag
        public GameObject prefab;
        public int initialSize;
        public PoolType poolType;
    }

    private class PooledObjectInfo : MonoBehaviour
    {
        public string key;
    }

    public interface IPooledObject
    {
        void OnSpawn(object param = null);
        void OnDespawn();
    }

    // =========================
    // State
    // =========================

    private readonly Dictionary<string, Queue<GameObject>> _pools = new();
    private readonly Dictionary<string, PoolConfig> _configs = new();
    private readonly Dictionary<PoolType, Transform> _categoryRoots = new();

    private Transform _root;

    // =========================
    // Constructor
    // =========================

    public ObjectPoolerManager()
    {
        InitializeRoots();
    }

    private void InitializeRoots()
    {
        _root = GameObject.Find("@Pools")?.transform;
        if (_root == null)
            _root = new GameObject("@Pools").transform;

        foreach (PoolType type in Enum.GetValues(typeof(PoolType)))
        {
            var go = new GameObject($"{type}Pool");
            go.transform.SetParent(_root);
            _categoryRoots[type] = go.transform;
        }
    }

    // =========================
    // Public Spawn API
    // =========================

    /// <summary>
    /// Spawn 요청
    /// - 풀 없으면 자동 로드 & 초기화
    /// </summary>
    public async UniTask<GameObject> SpawnAsync(
        string resourceKey,
        PoolType category,
        Vector3 position,
        Quaternion rotation,
        int initialSize = 3,
        object param = null
    )
    {
        await EnsurePoolAsync(resourceKey, category, initialSize);

        return SpawnInternal(resourceKey, position, rotation, param);
    }

    public async UniTask<T> SpawnAsync<T>(
        string resourceKey,
        PoolType category,
        Vector3 position,
        Quaternion rotation,
        int initialSize = 3,
        object param = null
    ) where T : Component
    {
        var go = await SpawnAsync(resourceKey, category, position, rotation, initialSize, param);
        return go != null && go.TryGetComponent(out T comp) ? comp : null;
    }

    // =========================
    // Despawn
    // =========================

    public void Despawn(GameObject obj)
    {
        if (obj == null) return;

        obj.GetComponent<IPooledObject>()?.OnDespawn();

        if (!obj.TryGetComponent(out PooledObjectInfo info) ||
            !_pools.TryGetValue(info.key, out var queue))
        {
            UnityEngine.Object.Destroy(obj);
            return;
        }

        obj.SetActive(false);
        obj.transform.SetParent(_categoryRoots[_configs[info.key].poolType], false);
        queue.Enqueue(obj);
    }

    // =========================
    // Pool Ensure (핵심)
    // =========================

    private async UniTask EnsurePoolAsync(
        string key,
        PoolType category,
        int initialSize
    )
    {
        if (_pools.ContainsKey(key))
            return;

        // 1️⃣ Prefab 로드 (AddressableManager 경유)
        var prefab = await Managers.AddressableManager
            .LoadAssetAsync<GameObject>(key);

        if (prefab == null)
        {
            Debug.LogError($"[Pooler] Prefab load failed: {key}");
            return;
        }

        // 2️⃣ PoolConfig 생성
        var config = new PoolConfig
        {
            key = key,
            prefab = prefab,
            initialSize = Mathf.Max(1, initialSize),
            poolType = category
        };

        RegisterPool(config);
    }

    // =========================
    // Pool Registration
    // =========================

    private void RegisterPool(PoolConfig config)
    {
        if (_configs.ContainsKey(config.key))
            return;

        _configs[config.key] = config;
        _pools[config.key] = new Queue<GameObject>();

        for (int i = 0; i < config.initialSize; i++)
        {
            var obj = CreateInstance(config);
            ReturnToPool(obj);
        }

        Debug.Log($"[Pooler] Pool initialized: {config.key} ({config.poolType})");
    }

    // =========================
    // Internal Spawn
    // =========================

    private GameObject SpawnInternal(
        string key,
        Vector3 position,
        Quaternion rotation,
        object param
    )
    {
        if (!_pools.TryGetValue(key, out var queue))
        {
            Debug.LogError($"[Pooler] Pool not found: {key}");
            return null;
        }

        if (queue.Count == 0)
        {
            var obj = CreateInstance(_configs[key]);
            queue.Enqueue(obj);
        }

        var instance = queue.Dequeue();
        instance.transform.SetPositionAndRotation(position, rotation);
        instance.SetActive(true);

        instance.GetComponent<IPooledObject>()?.OnSpawn(param);
        return instance;
    }

    // =========================
    // Instance Create / Return
    // =========================

    private GameObject CreateInstance(PoolConfig config)
    {
        var obj = UnityEngine.Object.Instantiate(config.prefab);
        obj.name = config.key;
        obj.SetActive(false);

        var info = obj.AddComponent<PooledObjectInfo>();
        info.key = config.key;

        obj.transform.SetParent(_categoryRoots[config.poolType], false);
        return obj;
    }

    private void ReturnToPool(GameObject obj)
    {
        if (!obj.TryGetComponent(out PooledObjectInfo info))
        {
            UnityEngine.Object.Destroy(obj);
            return;
        }

        obj.SetActive(false);
        obj.transform.SetParent(_categoryRoots[_configs[info.key].poolType], false);
        _pools[info.key].Enqueue(obj);
    }

    // =========================
    // Debug
    // =========================

    public void LogStatus()
    {
        foreach (var kv in _pools)
        {
            Debug.Log($"[Pool] {kv.Key} : {kv.Value.Count}");
        }
    }
}
