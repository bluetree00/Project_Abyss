using System;
using System.Collections.Generic;
using UnityEngine;

public class ObjectPoolerManager
{
    private Dictionary<string, Queue<GameObject>> poolDictionary; // 태그와 객체 큐를 매핑
    private List<GameObject> spawnObjects; // 생성된 객체들을 저장할 리스트
    private List<string> tags; // 태그를 저장할 리스트
    private Pool[] pools; // 초기화할 풀 정보
    private GameObject parentObject ; // 부모 오브젝트
    

    [Serializable]
    public class Pool
    {
        public string tag; // 태그 이름
        public string resourcePath; // 리소스 매니저에서 사용할 경로
        public int initialSize; // 초기 객체 수
    }

    //생성자
    public ObjectPoolerManager(Pool[] pools)
    {
        this.pools = pools;
        poolDictionary = new Dictionary<string, Queue<GameObject>>();
        spawnObjects = new List<GameObject>();
        tags = new List<string>();

        // 부모 오브젝트 생성
        parentObject = new GameObject("EffectPool");
        
        foreach (Pool pool in pools)
        {
            InitializePool(pool);
            AddNewTag(pool.tag); // 초기화할 때 태그 추가
        }
    }

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
        spawnObjects.Add(obj); // 생성된 객체를 리스트에 추가

        // 부모 오브젝트의 자식으로 설정
        obj.transform.SetParent(parentObject.transform);
        
        return obj;
    }

    // 새로운 태그를 추가하는 메서드
    public void AddNewTag(string tag)
    {
        if (!tags.Contains(tag))
        {
            tags.Add(tag); // 태그 리스트에 추가
            Debug.Log($"Tag '{tag}' added.");
        }
    }

    // 풀에서 객체를 생성하는 메서드
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

    // T 타입의 컴포넌트를 가진 객체를 생성하는 메서드 주로 컴포넌트를 가지고있는 오브젝트를 사용할때 사용하면 될듯.
    //예를들어 풀러에 있는 오브젝트에 바로 힘을 주고 싶을때 Rigidbody rb = bullet.GetComponent<Rigidbody>(); 처럼 참조를 거치지 않고
    // Rigidbody rb = objectPoolerManager.SpawnFromPool<Rigidbody>("Bullet", new Vector3(0, 0, 0), Quaternion.identity); 이런식으로 바로 사용하면 됨
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

    // 객체를 풀로 반환하는 메서드
    public void ReturnToPool(GameObject obj)
    {
        obj.SetActive(false);
        
        // Debug 로그 추가
        if (!poolDictionary.ContainsKey(obj.name))
        {
            GameObject.Destroy(obj);
        }
        else
        {
            poolDictionary[obj.name].Enqueue(obj);
        }
    }

    // 풀의 상태를 로그로 출력하는 메서드 확인용
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
