using System;
using System.Collections.Generic;
using System.Threading.Tasks;
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

    // 내부: pooled object에 붙여 원래 태그 보관
    private class PooledObjectInfo : MonoBehaviour
    {
        public string originalTag;
    }

    // (선택) pooled 오브젝트에 구현하면 호출되는 인터페이스
    public interface IPooledObject
    {
        void OnSpawn(object param = null);
        void OnDespawn();
    }

    // ObjectPoolerManager 초기화 시 풀들을 매개변수로 받아 초기화
    public ObjectPoolerManager(Pool[] pools)
    {
        this.pools = pools ?? Array.Empty<Pool>();
        poolDictionary = new Dictionary<string, Queue<GameObject>>();  // 풀 딕셔너리 초기화
        spawnObjects = new List<GameObject>(); // 생성된 오브젝트 목록 초기화
        tags = new List<string>(); // 태그 목록 초기화
        parentObjects = new Dictionary<PoolType, GameObject>(); // 부모 오브젝트 관리 딕셔너리 초기화

        // 루트(@Managers)가 있으면 그 아래에 풀 부모를 둠(정리 용이)
        GameObject root = GameObject.Find("@Managers");
        if (root == null)
            root = new GameObject("@Managers");

        // 각 풀 타입별 부모 오브젝트 생성(공간 정리용)
        parentObjects[PoolType.Effect] = new GameObject("EffectPool");
        parentObjects[PoolType.Monster] = new GameObject("MonsterPool");
        parentObjects[PoolType.Character] = new GameObject("CharacterPool");

        parentObjects[PoolType.Effect].transform.SetParent(root.transform, false);
        parentObjects[PoolType.Monster].transform.SetParent(root.transform, false);
        parentObjects[PoolType.Character].transform.SetParent(root.transform, false);

        // 초기 풀들 등록
        RegisterPools(this.pools);
    }

    /// <summary>
    /// 새로운 풀 목록을 등록(병합)합니다. 이미 존재하는 태그는 스킵합니다.
    /// Managers.InitializeWeaponEffectPoolsAsync 등에서 여러 번 호출될 때 사용.
    /// </summary>
    public void RegisterPools(Pool[] newPools)
    {
        if (newPools == null || newPools.Length == 0) return;

        var mergedList = new List<Pool>(pools ?? Array.Empty<Pool>());

        foreach (var pool in newPools)
        {
            if (pool == null || string.IsNullOrEmpty(pool.tag))
                continue;

            // 이미 존재하면 스킵
            if (poolDictionary.ContainsKey(pool.tag))
                continue;

            // 큐 생성
            poolDictionary[pool.tag] = new Queue<GameObject>();

            // 초기 생성(동기)
            for (int i = 0; i < Mathf.Max(1, pool.initialSize); i++)
            {
                GameObject obj = CreateNewObject(pool.tag, pool.resourcePath, pool.poolType);
                if (obj != null)
                    ReturnToPoolInternal(obj); // 내부 반환(비활성화 후 enqueue)
            }

            AddNewTag(pool.tag);
            mergedList.Add(pool);
        }

        // pools 배열 갱신 (병합 결과 저장)
        pools = mergedList.ToArray();
    }

    /// <summary>
    /// 해당 태그의 풀이 존재하는지 확인합니다.
    /// </summary>
    public bool HasPool(string tag)
    {
        if (string.IsNullOrEmpty(tag)) return false;
        return poolDictionary.ContainsKey(tag);
    }

    // 특정 풀을 초기화하는 메서드 (기존 InitializePool과 유사)
    private void InitializePool(Pool pool)
    {
        if (pool == null || string.IsNullOrEmpty(pool.tag)) return;

        // 이미 풀에 해당 태그가 존재하면 초기화를 건너뜀
        if (poolDictionary.ContainsKey(pool.tag))
        {
            Debug.LogWarning($"[ObjectPooler] 이미 존재하는 풀: {pool.tag}, 초기화를 건너뜀");
            return;
        }

        poolDictionary[pool.tag] = new Queue<GameObject>(); // 큐를 생성하여 풀에 할당

        // 초기 크기만큼 오브젝트를 생성하여 풀에 넣음
        for (int i = 0; i < Mathf.Max(1, pool.initialSize); i++)
        {
            GameObject obj = CreateNewObject(pool.tag, pool.resourcePath, pool.poolType);
            if (obj != null)
                ReturnToPoolInternal(obj);  // 내부 반환
        }

        AddNewTag(pool.tag);
    }

    // 오브젝트 생성 메서드 (동기 로드 사용: 블로킹 주의)
    private GameObject CreateNewObject(string tag, string resourcePath, PoolType poolType)
    {
        if (string.IsNullOrEmpty(tag) || string.IsNullOrEmpty(resourcePath))
        {
            Debug.LogWarning($"CreateNewObject invalid args: tag={tag}, path={resourcePath}");
            return null;
        }

        // AddressablesManager 동기 로드 사용 (원래 코드 유지). 블로킹 가능성 주의.
        GameObject prefab = Managers.AddressableManager.LoadAssetSync<GameObject>(resourcePath);
        if (prefab == null)
        {
            Debug.LogError($"Prefab at path {resourcePath} not found. (tag={tag})");
            return null;
        }

        GameObject obj = GameObject.Instantiate(prefab);
        obj.name = tag; // 기본 이름은 tag로 설정
        obj.SetActive(false);
        spawnObjects.Add(obj);

        // PooledObjectInfo로 원래 태그 보관
        var info = obj.GetComponent<PooledObjectInfo>();
        if (info == null) info = obj.AddComponent<PooledObjectInfo>();
        info.originalTag = tag;

        // 부모 오브젝트 설정 (풀 타입에 맞는 부모 오브젝트에 자식으로 넣음)
        if (parentObjects.TryGetValue(poolType, out var parent))
            obj.transform.SetParent(parent.transform, false);

        return obj;
    }

    #region 비동기 메서드(주석 처리된 기존 코드 보관)
    // 기존에 비동기 버전을 주석으로 보관해 두셨던 부분은 필요하면 비동기 형태로 복원 가능.
    #endregion

    // 새로운 태그를 태그 목록에 추가하는 메서드
    public void AddNewTag(string tag)
    {
        if (!tags.Contains(tag))
        {
            tags.Add(tag);
            Debug.Log($"Tag '{tag}' added.");
        }
    }

    // 풀에서 오브젝트를 꺼내는 메서드 (기존 SpawnFromPool 유지 - public)
    public GameObject SpawnFromPool(string tag, Vector3 position, Quaternion rotation)
    {
        if (string.IsNullOrEmpty(tag))
            return null;

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
            // 동기 초기화 (안전하게 RegisterPools 사용)
            RegisterPools(new Pool[] { newPool });
        }

        // 풀이 비어있으면 새 오브젝트를 생성하여 풀에 넣음
        if (poolDictionary[tag].Count == 0)
        {
            Pool pool = Array.Find(pools, x => x != null && x.tag == tag);
            if (pool != null)
            {
                GameObject obj = CreateNewObject(pool.tag, pool.resourcePath, pool.poolType);
                if (obj != null)
                    poolDictionary[tag].Enqueue(obj);  // 풀에 오브젝트 추가
            }
            else
            {
                // pools에 정보가 없으면 기본 생성 시도
                GameObject obj = CreateNewObject(tag, $"Effects/{tag}", PoolType.Effect);
                if (obj != null)
                    poolDictionary[tag].Enqueue(obj);
            }
        }

        // 풀에서 오브젝트를 꺼내고 위치와 회전 설정 후 활성화
        GameObject objectToSpawn = poolDictionary[tag].Dequeue();
        objectToSpawn.transform.position = position;
        objectToSpawn.transform.rotation = rotation;
        objectToSpawn.SetActive(true);

        // IPooledObject가 있으면 OnSpawn 호출 (초기화 파라미터 없음)
        var pooledComp = objectToSpawn.GetComponent<IPooledObject>();
        pooledComp?.OnSpawn(null);

        return objectToSpawn;
    }

    // 무기 풀에서 무기를 꺼내는 메서드 (호환성)
    public GameObject SpawnWeaponFromPool(string weaponKey, Vector3 position, Quaternion rotation)
    {
        return SpawnFromPool(weaponKey, position, rotation);  // 무기 풀에서 무기를 꺼냄
    }

    // 제네릭 방식으로 컴포넌트를 가져오는 메서드
    public T SpawnFromPool<T>(string tag, Vector3 position, Quaternion rotation) where T : Component
    {
        GameObject objectToSpawn = SpawnFromPool(tag, position, rotation);
        if (objectToSpawn == null) return null;

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

    // 외부에서 오브젝트 반환 시 사용(공개)
    public void Despawn(GameObject obj)
    {
        if (obj == null) return;

        // IPooledObject.OnDespawn 호출
        var pooledComp = obj.GetComponent<IPooledObject>();
        pooledComp?.OnDespawn();

        // 내부 반환 처리
        ReturnToPoolInternal(obj);
    }

    // 내부 반환: 이름(tag)으로 찾지 말고 PooledObjectInfo에서 원래 태그를 읽음
    private void ReturnToPoolInternal(GameObject obj)
    {
        if (obj == null) return;

        obj.SetActive(false);

        var info = obj.GetComponent<PooledObjectInfo>();
        string tag = info != null && !string.IsNullOrEmpty(info.originalTag) ? info.originalTag : obj.name;

        if (!poolDictionary.ContainsKey(tag))
        {
            // 풀 없으면 제거 (또는 풀을 새로 만들지 선택)
            GameObject.Destroy(obj);
            spawnObjects.Remove(obj);
            return;
        }

        // 부모를 해당 풀 부모로 되돌리기
        PoolType poolType = FindPoolTypeForTag(tag);
        if (parentObjects.TryGetValue(poolType, out var parent))
        {
            obj.transform.SetParent(parent.transform, false);
        }

        poolDictionary[tag].Enqueue(obj);
    }

    // 기존 ReturnToPool(외부 API 호환성 유지) - 이름 그대로 사용 가능
    public void ReturnToPool(GameObject obj)
    {
        Despawn(obj);
    }

    // 풀 상태를 로그로 출력하는 메서드
    public void LogPoolStatus()
    {
        foreach (Pool pool in pools)
        {
            if (pool == null) continue;
            int count = poolDictionary.ContainsKey(pool.tag) ? poolDictionary[pool.tag].Count : 0;
            Debug.Log($"{pool.tag} Pool: {count} / totalSpawned:{spawnObjects.Count}");
        }
    }

    // helper: pools 배열에서 태그의 poolType 찾기
    private PoolType FindPoolTypeForTag(string tag)
    {
        var p = Array.Find(pools, x => x != null && x.tag == tag);
        return p != null ? p.poolType : PoolType.Effect;
    }
}
