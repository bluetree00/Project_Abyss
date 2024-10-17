using System;
using System.Collections.Generic;
using UnityEngine;

public class ObjectPoolerManager
{
    private Dictionary<string, Queue<GameObject>> poolDictionary;
    private List<GameObject> spawnObjects;
    private Pool[] pools;

    [Serializable]
    public class Pool
    {
        public string tag;
        public string resourcePath; // 리소스 매니저에서 사용할 경로
        public int initialSize;
    }

    public ObjectPoolerManager(Pool[] pools)
    {
        this.pools = pools;
        poolDictionary = new Dictionary<string, Queue<GameObject>>();
        spawnObjects = new List<GameObject>();

        foreach (Pool pool in pools)
        {
            InitializePool(pool);
        }
    }

    // 특정 Pool을 초기화하여 미리 오브젝트를 생성하는 메서드
    private void InitializePool(Pool pool)
    {
        poolDictionary[pool.tag] = new Queue<GameObject>();

        for (int i = 0; i < pool.initialSize; i++)
        {
            GameObject obj = CreateNewObject(pool.tag, pool.resourcePath);
            ReturnToPool(obj); // 초기 오브젝트를 풀에 추가
        }
    }

    private GameObject CreateNewObject(string tag, string resourcePath)
    {
        GameObject prefab = Managers.Resource.Load<GameObject>($"Prefabs/{resourcePath}");
        if (prefab == null)
        {
            Debug.LogError($"Prefab at path {resourcePath} not found.");
            return null;
        }

        GameObject obj = GameObject.Instantiate(prefab);
        obj.name = tag;
        obj.SetActive(false);
        spawnObjects.Add(obj);
        return obj;
    }

    public GameObject SpawnFromPool(string tag, Vector3 position, Quaternion rotation)
    {
        // 풀에 태그가 존재하지 않는 경우 초기화
        if (!poolDictionary.ContainsKey(tag))
        {
            Pool newPool = new Pool { tag = tag, resourcePath = $"Effects/{tag}", initialSize = 1 };
            InitializePool(newPool);  // 동적으로 풀 초기화
        }

        if (poolDictionary[tag].Count == 0)
        {
            // 풀에 오브젝트가 부족할 경우 새 오브젝트 생성 후 추가
            Pool pool = Array.Find(pools, x => x.tag == tag);
            if (pool != null)
            {
                GameObject obj = CreateNewObject(pool.tag, pool.resourcePath);
                if (obj != null)
                    poolDictionary[tag].Enqueue(obj);
            }
        }

        GameObject objectToSpawn = poolDictionary[tag].Dequeue();
        objectToSpawn.transform.position = position;
        objectToSpawn.transform.rotation = rotation;
        objectToSpawn.SetActive(true);

        return objectToSpawn;
    }

    public T SpawnFromPool<T>(string tag, Vector3 position, Quaternion rotation) where T : Component
    {
        GameObject objectToSpawn = SpawnFromPool(tag, position, rotation);
        if (objectToSpawn.TryGetComponent(out T component))
        {
            return component;
        }
        else
        {
            ReturnToPool(objectToSpawn);
            throw new Exception($"Component {typeof(T)} not found on pooled object with tag {tag}");
        }
    }

    public void ReturnToPool(GameObject obj)
    {
        obj.SetActive(false);
        if (!poolDictionary.ContainsKey(obj.name))
        {
            Debug.LogWarning($"Pool with tag {obj.name} doesn't exist. Object destroyed instead of returned to pool.");
            GameObject.Destroy(obj);
        }
        else
        {
            poolDictionary[obj.name].Enqueue(obj);
        }
    }

    public void LogPoolStatus()
    {
        foreach (var pool in pools)
        {
            if (poolDictionary.ContainsKey(pool.tag))
            {
                Debug.Log($"{pool.tag} Pool: {poolDictionary[pool.tag].Count} / {spawnObjects.Count}");
            }
        }
    }
}
