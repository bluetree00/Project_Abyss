using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace RelicFairy.Monster
{
public static class BossEffectPool
{
    private static readonly Dictionary<int, Queue<GameObject>> Pools = new();
    private static readonly Dictionary<int, Transform> Containers = new();

    private static Transform _root;
    private static Transform _effectRoot;
    private static readonly Vector3 HiddenPosition = new Vector3(0f, -1000f, 0f);

    public static GameObject Spawn(
        GameObject prefab,
        Vector3 position,
        Quaternion rotation,
        Transform parent = null,
        bool worldPositionStays = true)
    {
        if (prefab == null)
            return null;

        try
        {
            EnsureRoots();
            var instance = GetOrCreate(prefab);
            if (instance == null)
                return SpawnFallback(prefab, position, rotation, parent, worldPositionStays);

            var pooled = instance.GetComponent<BossPooledEffect>();
            if (pooled == null)
                pooled = instance.AddComponent<BossPooledEffect>();

            pooled.SourcePrefab ??= prefab;
            pooled.CancelScheduledRelease();

            if (parent != null)
                instance.transform.SetParent(parent, worldPositionStays);
            else
                instance.transform.SetParent(_effectRoot, true);

            instance.transform.SetPositionAndRotation(position, rotation);
            instance.SetActive(true);
            pooled.ResetVisuals();
            return instance;
        }
        catch
        {
            return SpawnFallback(prefab, position, rotation, parent, worldPositionStays);
        }
    }

    public static GameObject SpawnOneShot(
        GameObject prefab,
        Vector3 position,
        Quaternion rotation,
        Transform parent = null,
        float fallbackLifetime = 2f,
        bool worldPositionStays = true)
    {
        var instance = Spawn(prefab, position, rotation, parent, worldPositionStays);
        if (instance == null)
            return null;

        ScheduleRelease(instance, CalculateLifetime(prefab, fallbackLifetime));
        return instance;
    }

    public static void ScheduleRelease(GameObject instance, float delay)
    {
        if (instance == null)
            return;

        if (instance.TryGetComponent(out BossPooledEffect pooled))
            pooled.ScheduleRelease(delay);
    }

    public static void Release(GameObject instance)
    {
        if (instance == null)
            return;

        if (!instance.TryGetComponent(out BossPooledEffect pooled) || pooled.SourcePrefab == null)
        {
            Object.Destroy(instance);
            return;
        }

        pooled.CancelScheduledRelease();

        int key = pooled.SourcePrefab.GetInstanceID();
        EnsureRoots();
        EnsureContainer(pooled.SourcePrefab, key);
        if (!instance.activeSelf && instance.transform.parent == Containers[key])
            return;

        instance.SetActive(false);
        instance.transform.SetParent(Containers[key], false);
        instance.transform.localPosition = HiddenPosition;

        if (!Pools.TryGetValue(key, out var queue))
        {
            queue = new Queue<GameObject>();
            Pools[key] = queue;
        }

        queue.Enqueue(instance);
    }

    public static float CalculateLifetime(GameObject prefab, float fallbackLifetime = 2f)
    {
        if (prefab == null)
            return fallbackLifetime;

        float maxLifetime = 0f;
        var particles = prefab.GetComponentsInChildren<ParticleSystem>(true);
        foreach (var particle in particles)
        {
            var main = particle.main;
            float duration = main.loop ? fallbackLifetime : main.duration + GetStartLifetime(main.startLifetime, fallbackLifetime);
            maxLifetime = Mathf.Max(maxLifetime, duration);
        }

        return maxLifetime > 0.05f ? maxLifetime + 0.25f : fallbackLifetime;
    }

    private static GameObject GetOrCreate(GameObject prefab)
    {
        int key = prefab.GetInstanceID();
        if (Pools.TryGetValue(key, out var queue))
        {
            while (queue.Count > 0)
            {
                var pooled = queue.Dequeue();
                if (pooled != null)
                    return pooled;
            }
        }

        EnsureContainer(prefab, key);

        var created = Object.Instantiate((Object)prefab, Vector3.zero, Quaternion.identity);
        var instance = created as GameObject;
        if (instance == null && created is Component component)
            instance = component.gameObject;
        if (instance == null)
            return null;

        instance.name = prefab.name;
        instance.transform.SetParent(Containers[key], false);
        instance.SetActive(false);
        instance.transform.localPosition = HiddenPosition;

        var pooledEffect = instance.GetComponent<BossPooledEffect>();
        if (pooledEffect == null)
            pooledEffect = instance.AddComponent<BossPooledEffect>();

        pooledEffect.SourcePrefab = prefab;
        return instance;
    }

    private static GameObject SpawnFallback(
        GameObject prefab,
        Vector3 position,
        Quaternion rotation,
        Transform parent,
        bool worldPositionStays)
    {
        var created = Object.Instantiate((Object)prefab, position, rotation);
        var instance = created as GameObject;
        if (instance == null && created is Component component)
            instance = component.gameObject;
        if (instance == null)
            return null;

        if (parent != null)
            instance.transform.SetParent(parent, worldPositionStays);

        return instance;
    }

    private static void EnsureContainer(GameObject prefab, int key)
    {
        EnsureRoots();
        if (Containers.ContainsKey(key))
            return;

        var container = new GameObject(prefab.name).transform;
        container.SetParent(_effectRoot, false);
        Containers[key] = container;
    }

    private static void EnsureRoots()
    {
        if (_root == null)
        {
            _root = GameObject.Find("@Pools")?.transform;
            if (_root == null)
                _root = new GameObject("@Pools").transform;
        }

        if (_effectRoot == null)
        {
            var existingEffectRoot = _root.Find("EffectPool");
            if (existingEffectRoot == null)
                existingEffectRoot = new GameObject("EffectPool").transform;

            existingEffectRoot.SetParent(_root, false);

            _effectRoot = existingEffectRoot.Find("BossEffects");
            if (_effectRoot == null)
            {
                _effectRoot = new GameObject("BossEffects").transform;
                _effectRoot.SetParent(existingEffectRoot, false);
            }
        }
    }

    private static float GetStartLifetime(ParticleSystem.MinMaxCurve lifetime, float fallback)
    {
        return lifetime.mode switch
        {
            ParticleSystemCurveMode.Constant => lifetime.constant,
            ParticleSystemCurveMode.TwoConstants => lifetime.constantMax,
            _ => fallback
        };
    }
}

public sealed class BossPooledEffect : MonoBehaviour
{
    public GameObject SourcePrefab { get; set; }

    private Coroutine     _releaseRoutine;
    private TrailRenderer[]  _cachedTrails;
    private ParticleSystem[] _cachedParticles;

    public void ScheduleRelease(float delay)
    {
        CancelScheduledRelease();
        if (delay < 0f || !gameObject.activeInHierarchy)
            return;

        _releaseRoutine = StartCoroutine(ReleaseAfterDelay(delay));
    }

    public void CancelScheduledRelease()
    {
        if (_releaseRoutine == null)
            return;

        StopCoroutine(_releaseRoutine);
        _releaseRoutine = null;
    }

    public void ResetVisuals()
    {
        _cachedTrails    ??= GetComponentsInChildren<TrailRenderer>(true);
        _cachedParticles ??= GetComponentsInChildren<ParticleSystem>(true);

        foreach (var trail in _cachedTrails)
            trail.Clear();

        foreach (var particle in _cachedParticles)
        {
            particle.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            particle.Play(true);
        }
    }

    private IEnumerator ReleaseAfterDelay(float delay)
    {
        yield return new WaitForSeconds(delay);
        _releaseRoutine = null;
        BossEffectPool.Release(gameObject);
    }

    private void OnDisable()
    {
        CancelScheduledRelease();
    }
}

public static class QuadTilePool
{
    private static readonly Queue<(GameObject go, MeshRenderer mr, Material mat)> _pool = new();
    private static Shader    _shader;
    private static Mesh      _quadMesh;
    private static Transform _container;

    private static Shader GetShader()
    {
        if (_shader == null)
            _shader = Shader.Find("Sprites/Default")
                   ?? Shader.Find("Universal Render Pipeline/Particles/Unlit");
        return _shader;
    }

    private static Mesh GetQuadMesh()
    {
        if (_quadMesh != null) return _quadMesh;
        var temp = GameObject.CreatePrimitive(PrimitiveType.Quad);
        _quadMesh = temp.GetComponent<MeshFilter>().sharedMesh;
        Object.Destroy(temp);
        return _quadMesh;
    }

    private static void EnsureContainer()
    {
        if (_container != null) return;
        var existing = GameObject.Find("@QuadTilePool");
        if (existing == null)
        {
            existing = new GameObject("@QuadTilePool");
            Object.DontDestroyOnLoad(existing);
        }
        _container = existing.transform;
    }

    /// <summary>전투 전 초기화 시점에 호출해 풀을 사전 생성한다. 전투 중 첫 Rent 시 렉 방지.</summary>
    public static void Prewarm(int count)
    {
        EnsureContainer();
        // 이전 씬 리로드로 파괴된 null 항목 정리 후 유효 항목만 남김
        int existing = _pool.Count;
        int validCount = 0;
        for (int i = 0; i < existing; i++)
        {
            var item = _pool.Dequeue();
            if (item.go != null) { _pool.Enqueue(item); validCount++; }
        }
        for (int i = validCount; i < count; i++)
            _pool.Enqueue(CreateNew());
    }

    public static (GameObject go, MeshRenderer mr, Material mat) Rent()
    {
        while (_pool.Count > 0)
        {
            var item = _pool.Dequeue();
            if (item.go != null) { item.mr.enabled = true; return item; }
        }
        var newItem = CreateNew();
        newItem.mr.enabled = true;
        return newItem;
    }

    private static (GameObject go, MeshRenderer mr, Material mat) CreateNew()
    {
        EnsureContainer();
        var go = new GameObject("PooledQuad");
        go.transform.SetParent(_container, false);
        var mf = go.AddComponent<MeshFilter>();
        mf.sharedMesh = GetQuadMesh();
        var mr = go.AddComponent<MeshRenderer>();
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows    = false;
        mr.enabled           = false; // 풀 대기 상태 — Rent 시 enabled=true로 전환
        var mat = new Material(GetShader());
        mr.sharedMaterial    = mat;
        return (go, mr, mat);
    }

    public static void Return(GameObject go, MeshRenderer mr, Material ownedMat)
    {
        if (go == null) return;
        go.name = "PooledQuad";
        if (_container != null) go.transform.SetParent(_container, false);
        if (mr != null) { mr.sharedMaterial = ownedMat; mr.enabled = false; }
        if (ownedMat != null) ownedMat.color = Color.clear;
        _pool.Enqueue((go, mr, ownedMat));
    }
}
}
