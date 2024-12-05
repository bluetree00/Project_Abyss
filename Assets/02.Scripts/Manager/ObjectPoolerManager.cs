using System;
using System.Collections.Generic;
using UnityEngine;

public class ObjectPoolerManager 
{
    private Dictionary<string, Queue<GameObject>> poolDictionary;
    private List<GameObject> spawnObjects;
    private List<string> tags;
    private Dictionary<PoolType, GameObject> parentObjects;  // 부모 오브젝트를 풀 타입별로 관리
    private Pool[] pools;

    // 열거형으로 풀 타입 정의
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
        public string resourcePath;
        public int initialSize;
        public PoolType poolType;
    }

    public ObjectPoolerManager(Pool[] pools)
    {
        this.pools = pools;
        poolDictionary = new Dictionary<string, Queue<GameObject>>();
        spawnObjects = new List<GameObject>();
        tags = new List<string>();
        parentObjects = new Dictionary<PoolType, GameObject>();

        // 풀 타입별 부모 오브젝트 생성
        parentObjects[PoolType.Effect] = new GameObject("EffectPool");
        parentObjects[PoolType.Monster] = new GameObject("MonsterPool");
        parentObjects[PoolType.Character] = new GameObject("CharacterPool");

        // 풀 초기화
        foreach (Pool pool in pools)
        {
            InitializePool(pool);
            AddNewTag(pool.tag);
        }
    }

    private void InitializePool(Pool pool)
    {
        poolDictionary[pool.tag] = new Queue<GameObject>();

        for (int i = 0; i < pool.initialSize; i++)
        {
            GameObject obj = CreateNewObject(pool.tag, pool.resourcePath, pool.poolType);
            ReturnToPool(obj);
        }
    }

    private GameObject CreateNewObject(string tag, string resourcePath, PoolType poolType)
    {
         //Debug.Log($"Loading prefab from path: {resourcePath}");
        GameObject prefab = Managers.Resource.Load<GameObject>($"Prefabs/{resourcePath}");
        if (prefab == null)
        {
            //Debug.LogError($"Prefab at path {resourcePath} not found.");
            return null;
        }

        GameObject obj = GameObject.Instantiate(prefab);
        obj.name = tag;
        obj.SetActive(false);
        spawnObjects.Add(obj);

        // 부모 오브젝트 설정
        obj.transform.SetParent(parentObjects[poolType].transform);
        return obj;
    }

    public void AddNewTag(string tag)
    {
        if (!tags.Contains(tag))
        {
            tags.Add(tag);
            Debug.Log($"Tag '{tag}' added.");
        }
    }

    public GameObject SpawnFromPool(string tag, Vector3 position, Quaternion rotation)
    {
        if (!poolDictionary.ContainsKey(tag))
        {
            Pool newPool = new Pool { tag = tag, resourcePath = $"Effects/{tag}", initialSize = 1, poolType = PoolType.Effect };
            InitializePool(newPool);
        }

        if (poolDictionary[tag].Count == 0)
        {
            Pool pool = Array.Find(pools, x => x.tag == tag);
            if (pool != null)
            {
                GameObject obj = CreateNewObject(pool.tag, pool.resourcePath, pool.poolType);
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

    public T SpawnFromPool<T>(string tag, Vector3 position, Quaternion rotation) where T : Component //오브젝트에 있는 컴포넌트를 이용하고 싶을때 사용 예를들어 rd 참조
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
