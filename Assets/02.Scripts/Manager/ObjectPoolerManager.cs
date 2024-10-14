using System.Collections.Generic;
using UnityEngine;
using System;

public class ObjectPoolerManager
{
    private Dictionary<string, Queue<GameObject>> poolDictionary;
    private List<GameObject> spawnObjects;

    [Serializable]
    public class Pool
    {
        public string tag;
        public GameObject prefab;
        public int size;
    }

    private Pool[] pools;

    public ObjectPoolerManager(Pool[] pools)
    {
        this.pools = pools;
        poolDictionary = new Dictionary<string, Queue<GameObject>>();
        spawnObjects = new List<GameObject>();

        foreach (Pool pool in pools)
        {
            poolDictionary[pool.tag] = new Queue<GameObject>();
            for (int i = 0; i < pool.size; i++)
            {
                var obj = CreateNewObject(pool.tag, pool.prefab);
                ArrangePool(obj);
            }
        }
    }

    private GameObject CreateNewObject(string tag, GameObject prefab)
    {
        var obj = UnityEngine.Object.Instantiate(prefab);
        obj.name = tag;
        obj.SetActive(false);
        return obj;
    }

    private void ArrangePool(GameObject obj)
    {
        spawnObjects.Add(obj);
        poolDictionary[obj.name].Enqueue(obj);
    }

    public GameObject SpawnFromPool(string tag, Vector3 position, Quaternion rotation)
    {
        if (!poolDictionary.ContainsKey(tag))
            throw new Exception($"Pool with tag {tag} doesn't exist.");

        Queue<GameObject> poolQueue = poolDictionary[tag];
        if (poolQueue.Count <= 0)
        {
            Pool pool = Array.Find(pools, x => x.tag == tag);
            var obj = CreateNewObject(pool.tag, pool.prefab);
            ArrangePool(obj);
        }

        GameObject objectToSpawn = poolQueue.Dequeue();
        objectToSpawn.transform.position = position;
        objectToSpawn.transform.rotation = rotation;
        objectToSpawn.SetActive(true);
        return objectToSpawn;
    }

    public void ReturnToPool(GameObject obj)
    {
        obj.SetActive(false);
        poolDictionary[obj.name].Enqueue(obj);
    }
}
