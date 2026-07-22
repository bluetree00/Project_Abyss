using System;
using System.Collections.Generic;
using UnityEngine;
using Cysharp.Threading.Tasks;

public interface IPooledObject
{
    void OnSpawn(object param = null);
    void OnDespawn();
}

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
        {
            _root = new GameObject("@Pools").transform;
            // 풀 오브젝트가 씬 전환으로 파괴되지 않도록 DDOL 등록.
            // StageMap 선행 프리웜으로 생성된 인스턴스가 GameScene에서도 재사용된다.
            UnityEngine.Object.DontDestroyOnLoad(_root.gameObject);
        }

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
        // [임시 추적] 초록 이펙트 출처 확인용 — 확인 후 제거할 것.
        if (resourceKey != null && (resourceKey.Contains("Dark") || resourceKey.Contains("Explosion") || resourceKey.Contains("Smoke")))
            Debug.Log($"[VFX추적] ObjectPooler.SpawnAsync: {resourceKey}\n{System.Environment.StackTrace}");

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

    /// <summary>
    /// Addressables 키 없이 프리팹 직접 참조로 동기 풀 스폰. (예: SO에 직접 할당된 히트 VFX 프리팹)
    /// 풀은 prefab 인스턴스ID로 키잉되어 동일 프리팹은 같은 풀을 재사용한다. Addressables 로드 없음.
    /// 풀이 비어 있으면 SpawnInternal이 즉시 1개 생성하므로 사전 프리웜 불필요.
    /// </summary>
    public GameObject SpawnFromPrefab(GameObject prefab, PoolType category, Vector3 position, Quaternion rotation, object param = null)
    {
        if (prefab == null) return null;

        string key = "prefab:" + prefab.GetInstanceID();
        if (!_pools.ContainsKey(key))
        {
            _configs[key] = new PoolConfig { key = key, prefab = prefab, initialSize = 1, poolType = category };
            _pools[key]   = new Queue<GameObject>();
        }
        return SpawnInternal(key, position, rotation, param);
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
            // 비풀 경로: AddressableManager.InstantiateAsync로 만든 추적 인스턴스(예: 보스)면
            // ReleaseInstance로 핸들까지 정식 해제(+GO 파괴)해 장부 누수를 막고, 아니면 일반 Destroy.
            if (Managers.AddressableManager != null && Managers.AddressableManager.IsTrackedInstance(obj))
                Managers.AddressableManager.ReleaseInstance(obj);
            else
                UnityEngine.Object.Destroy(obj);
            return;
        }

        obj.SetActive(false);
        obj.transform.SetParent(_categoryRoots[_configs[info.key].poolType], false);
        queue.Enqueue(obj);
    }

    // =========================
    // Prewarm (공개 API)
    // =========================

    /// <summary>
    /// 방 진입 전 호출해 풀을 미리 채워 첫 스폰 프레임 드랍을 방지한다.
    /// 이미 풀이 존재하면 no-op.
    /// </summary>
    public async UniTask PrewarmAsync(
        string key,
        PoolType category,
        int size,
        System.Threading.CancellationToken ct = default)
    {
        await EnsurePoolAsync(key, category, size, ct);
    }

    // =========================
    // Pool Ensure (핵심)
    // =========================

    private async UniTask EnsurePoolAsync(
        string key,
        PoolType category,
        int initialSize,
        System.Threading.CancellationToken ct = default
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

        await RegisterPoolAsync(config, ct);
    }

    // =========================
    // Pool Registration
    // =========================

    private async UniTask RegisterPoolAsync(PoolConfig config, System.Threading.CancellationToken ct = default)
    {
        if (_configs.ContainsKey(config.key))
            return;

        _configs[config.key] = config;
        _pools[config.key] = new Queue<GameObject>();

        const int yieldEvery = 3;
        for (int i = 0; i < config.initialSize; i++)
        {
            if (ct.IsCancellationRequested) break;
            var obj = CreateInstance(config);
            ReturnToPool(obj);
            if ((i + 1) % yieldEvery == 0)
                await UniTask.Yield();
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

        GameObject instance = null;
        while (instance == null)
        {
            if (queue.Count == 0)
            {
                instance = CreateInstance(_configs[key]);
                break;
            }
            instance = queue.Dequeue(); // 파괴된 오브젝트면 null → 다시 루프
        }

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
    // Clear (PR6: 스코프 언로드 선행 — 에셋 해제 전 풀 인스턴스 파괴)
    // =========================

    /// <summary>
    /// 지정 카테고리의 모든 풀을 비운다 — 유휴(큐) 인스턴스를 파괴하고 풀 등록을 제거한다.
    /// 사용 중(활성) 인스턴스는 Despawn 시 풀 미발견 경로로 자체 Destroy된다.
    /// ⚠ 이 풀의 프리팹 에셋을 Addressables에서 해제하기 전에 먼저 호출해야 한다(dangling 방지).
    /// </summary>
    public void ClearCategory(PoolType category)
    {
        var keysToRemove = new List<string>();
        foreach (var kv in _configs)
            if (kv.Value.poolType == category)
                keysToRemove.Add(kv.Key);

        foreach (var key in keysToRemove)
        {
            if (_pools.TryGetValue(key, out var queue))
            {
                while (queue.Count > 0)
                {
                    var obj = queue.Dequeue();
                    if (obj != null) UnityEngine.Object.Destroy(obj);
                }
                _pools.Remove(key);
            }
            _configs.Remove(key);
        }
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
