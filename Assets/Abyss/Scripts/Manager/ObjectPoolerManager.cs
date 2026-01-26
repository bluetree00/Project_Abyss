using System;
using System.Collections.Generic;
using UnityEngine;

public class ObjectPoolerManager
{
    private readonly Dictionary<string, Queue<GameObject>> poolDictionary = new();
    private readonly Dictionary<string, Pool> poolConfigs = new();
    private readonly Dictionary<PoolType, Transform> parentRoots = new();
    private readonly List<GameObject> spawnedObjects = new();

    public enum PoolType
    {
        Effect,
        Monster,
        Character
    }

    [Serializable]
    public class Pool
    {
        public string tag;
        public GameObject prefab;
        public int initialSize;
        public PoolType poolType;
    }

    private class PooledObjectInfo : MonoBehaviour
    {
        public string tag;
    }

    public interface IPooledObject
    {
        void OnSpawn(object param = null);
        void OnDespawn();
    }

    // =========================
    // 생성자
    // =========================
    public ObjectPoolerManager(IEnumerable<Pool> pools)
    {
        var root = GameObject.Find("@Pools") ?? new GameObject("@Pools");

        foreach (PoolType type in Enum.GetValues(typeof(PoolType)))
        {
            var go = new GameObject($"{type}Pool");
            go.transform.SetParent(root.transform);
            parentRoots[type] = go.transform;
        }

        RegisterPools(pools);
    }

    // =========================
    // Pool 등록
    // =========================
    public void RegisterPools(IEnumerable<Pool> pools)
    {
        if (pools == null) return;

        foreach (var pool in pools)
        {
            if (pool == null ||
                string.IsNullOrEmpty(pool.tag) ||
                pool.prefab == null ||
                poolDictionary.ContainsKey(pool.tag))
                continue;

            poolConfigs[pool.tag] = pool;
            poolDictionary[pool.tag] = new Queue<GameObject>();

            for (int i = 0; i < Mathf.Max(1, pool.initialSize); i++)
            {
                var obj = CreateInstance(pool);
                ReturnInternal(obj);
            }
        }
    }

    // =========================
    // Spawn
    // =========================
    public GameObject Spawn(string tag, Vector3 position, Quaternion rotation, object param = null)
    {
        if (!poolDictionary.TryGetValue(tag, out var queue))
        {
            Debug.LogError($"[Pooler] Pool not registered: {tag}");
            return null;
        }

        if (queue.Count == 0)
        {
            var pool = poolConfigs[tag];
            queue.Enqueue(CreateInstance(pool));
        }

        var obj = queue.Dequeue();
        obj.transform.SetPositionAndRotation(position, rotation);
        obj.SetActive(true);

        obj.GetComponent<IPooledObject>()?.OnSpawn(param);
        return obj;
    }

    public T Spawn<T>(string tag, Vector3 position, Quaternion rotation, object param = null)
        where T : Component
    {
        var go = Spawn(tag, position, rotation, param);
        return go != null && go.TryGetComponent(out T comp) ? comp : null;
    }

    // =========================
    // Despawn
    // =========================
    public void Despawn(GameObject obj)
    {
        if (obj == null) return;

        obj.GetComponent<IPooledObject>()?.OnDespawn();
        ReturnInternal(obj);
    }

    // =========================
    // 내부 구현
    // =========================
    private GameObject CreateInstance(Pool pool)
    {
        var obj = GameObject.Instantiate(pool.prefab);
        obj.name = pool.tag;
        obj.SetActive(false);

        var info = obj.AddComponent<PooledObjectInfo>();
        info.tag = pool.tag;

        obj.transform.SetParent(parentRoots[pool.poolType], false);
        spawnedObjects.Add(obj);

        return obj;
    }

    private void ReturnInternal(GameObject obj)
    {
        if (!obj.TryGetComponent(out PooledObjectInfo info) ||
            !poolDictionary.ContainsKey(info.tag))
        {
            GameObject.Destroy(obj);
            return;
        }

        obj.SetActive(false);
        obj.transform.SetParent(parentRoots[poolConfigs[info.tag].poolType], false);
        poolDictionary[info.tag].Enqueue(obj);
    }

    // =========================
    // Debug
    // =========================
    public void LogStatus()
    {
        foreach (var kv in poolDictionary)
        {
            Debug.Log($"[Pool] {kv.Key} : {kv.Value.Count}");
        }
    }
}
