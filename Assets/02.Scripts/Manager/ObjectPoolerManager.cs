using System;
using System.Collections.Generic;
using UnityEngine;

public class ObjectPoolerManager 
{
    private Dictionary<string, Queue<GameObject>> poolDictionary; // 풀을 관리하는 딕셔너리
    private List<GameObject> spawnObjects; // 스폰된 오브젝트들을 저장
    private List<string> tags; // 풀에 저장된 태그 목록
    private Dictionary<PoolType, GameObject> parentObjects; // 풀 타입별 부모 오브젝트 관리
    private Pool[] pools; // 초기화된 풀 배열

    // 풀 타입 열거형
    public enum PoolType
    {
        Effect,      // 이펙트 풀
        Monster,     // 몬스터 풀
        Character    // 캐릭터 풀
    }

    // 풀의 각종 속성을 정의한 클래스
    [Serializable]
    public class Pool
    {
        public string tag;           // 풀을 구분할 태그
        public string resourcePath;  // 해당 리소스의 경로
        public int initialSize;      // 풀의 초기 크기
        public PoolType poolType;    // 풀의 타입
    }

    // ObjectPoolerManager 초기화 시 풀들을 매개변수로 받아 초기화
    public ObjectPoolerManager(Pool[] pools)
    {
        this.pools = pools;
        poolDictionary = new Dictionary<string, Queue<GameObject>>();  // 풀 딕셔너리 초기화
        spawnObjects = new List<GameObject>(); // 생성된 오브젝트 목록 초기화
        tags = new List<string>(); // 태그 목록 초기화
        parentObjects = new Dictionary<PoolType, GameObject>(); // 부모 오브젝트 관리 딕셔너리 초기화

        // 각 풀 타입별 부모 오브젝트 생성
        parentObjects[PoolType.Effect] = new GameObject("EffectPool");
        parentObjects[PoolType.Monster] = new GameObject("MonsterPool");
        parentObjects[PoolType.Character] = new GameObject("CharacterPool");

        // 풀 초기화
        foreach (Pool pool in pools)
        {
            InitializePool(pool);  // 각 풀 초기화
            AddNewTag(pool.tag);   // 태그 추가
        }
    }

    // 특정 풀을 초기화하는 메서드
    private void InitializePool(Pool pool)
    {
        // 이미 풀에 해당 태그가 존재하면 초기화를 건너뜀
        if (poolDictionary.ContainsKey(pool.tag))
        {
            Debug.LogWarning($"[ObjectPooler] 이미 존재하는 풀: {pool.tag}, 초기화를 건너뜀");
            return;
        }

        poolDictionary[pool.tag] = new Queue<GameObject>(); // 큐를 생성하여 풀에 할당

        // 초기 크기만큼 오브젝트를 생성하여 풀에 넣음
        for (int i = 0; i < pool.initialSize; i++)
        {
            GameObject obj = CreateNewObject(pool.tag, pool.resourcePath, pool.poolType);
            ReturnToPool(obj);  // 풀로 반환
        }
    }

    // 오브젝트 생성 메서드
    private GameObject CreateNewObject(string tag, string resourcePath, PoolType poolType)
    {
        // AddressablesManager를 사용하여 리소스 경로로 프리팹을 동기적으로 로드
        GameObject prefab = AddressablesManager.Instance.LoadAssetSync<GameObject>(resourcePath);
        if (prefab == null)
        {
            Debug.LogError($"Prefab at path {resourcePath} not found.");
            return null;
        }

        // 오브젝트를 생성하고 비활성화 상태로 초기화
        GameObject obj = GameObject.Instantiate(prefab);
        obj.name = tag;
        obj.SetActive(false);
        spawnObjects.Add(obj);

        // 부모 오브젝트 설정 (풀 타입에 맞는 부모 오브젝트에 자식으로 넣음)
        obj.transform.SetParent(parentObjects[poolType].transform);
        return obj;
    }

    // 새로운 태그를 태그 목록에 추가하는 메서드
    public void AddNewTag(string tag)
    {
        if (!tags.Contains(tag))
        {
            tags.Add(tag);
            Debug.Log($"Tag '{tag}' added.");
        }
    }

    // 풀에서 오브젝트를 꺼내는 메서드
    public GameObject SpawnFromPool(string tag, Vector3 position, Quaternion rotation)
    {
        // 해당 태그가 등록되지 않은 경우 기본 설정으로 풀을 초기화
        if (!poolDictionary.ContainsKey(tag))
        {
            Pool newPool = new Pool
            {
                tag = tag,
                resourcePath = $"Effects/{tag}",
                initialSize = 1,
                poolType = PoolType.Effect
            };
            InitializePool(newPool); // 새 풀 초기화
        }

        // 풀이 비어있으면 새 오브젝트를 생성하여 풀에 넣음
        if (poolDictionary[tag].Count == 0)
        {
            Pool pool = Array.Find(pools, x => x.tag == tag);
            if (pool != null)
            {
                GameObject obj = CreateNewObject(pool.tag, pool.resourcePath, pool.poolType);
                if (obj != null)
                    poolDictionary[tag].Enqueue(obj);  // 풀에 오브젝트 추가
            }
        }

        // 풀에서 오브젝트를 꺼내고 위치와 회전 설정 후 활성화
        GameObject objectToSpawn = poolDictionary[tag].Dequeue();
        objectToSpawn.transform.position = position;
        objectToSpawn.transform.rotation = rotation;
        objectToSpawn.SetActive(true);
        return objectToSpawn;
    }

    // 무기 풀에서 무기를 꺼내는 메서드
    public GameObject SpawnWeaponFromPool(string weaponKey, Vector3 position, Quaternion rotation)
    {
        return SpawnFromPool(weaponKey, position, rotation);  // 무기 풀에서 무기를 꺼냄
    }

    // 제네릭 방식으로 컴포넌트를 가져오는 메서드
    public T SpawnFromPool<T>(string tag, Vector3 position, Quaternion rotation) where T : Component
    {
        GameObject objectToSpawn = SpawnFromPool(tag, position, rotation);
        if (objectToSpawn.TryGetComponent(out T component))
        {
            return component; // 컴포넌트를 반환
        }
        else
        {
            ReturnToPool(objectToSpawn);  // 컴포넌트가 없으면 풀에 반환
            throw new Exception($"Component {typeof(T)} not found on pooled object with tag {tag}");
        }
    }

    // 오브젝트를 풀로 반환하는 메서드
    public void ReturnToPool(GameObject obj)
    {
        obj.SetActive(false);  // 비활성화 후 풀에 반환

        if (!poolDictionary.ContainsKey(obj.name))  // 해당 태그의 풀이 없으면 오브젝트 삭제
        {
            GameObject.Destroy(obj);
        }
        else
        {
            poolDictionary[obj.name].Enqueue(obj);  // 풀에 오브젝트 추가
        }
    }

    // 풀 상태를 로그로 출력하는 메서드
    public void LogPoolStatus()
    {
        foreach (Pool pool in pools)
        {
            if (poolDictionary.ContainsKey(pool.tag))
            {
                Debug.Log($"{pool.tag} Pool: {poolDictionary[pool.tag].Count} / {spawnObjects.Count}");
            }
        }
    }
}
