using System.Collections.Generic;
using UnityEngine;
using Cysharp.Threading.Tasks;
using UnityEngine.AddressableAssets;

public static class ObjectPoolDataInitializer
{
    /// <summary>
    /// 키 기반 PoolDataPackage 로드 후 ObjectPoolerManager용 Pool 생성
    /// </summary>
    public static async UniTask<List<ObjectPoolerManager.Pool>> GetPoolsAsync(string packageKey)
    {
        if (string.IsNullOrEmpty(packageKey)) return new List<ObjectPoolerManager.Pool>();

        // 1. AddressableManager를 사용해 PoolDataPackage 로드
        PoolDataPackage package = await Managers.AddressableManager.LoadAssetAsync<PoolDataPackage>(packageKey);
        if (package == null || package.Pools.Count == 0)
        {
            Debug.LogWarning($"[ObjectPoolDataInitializer] PoolDataPackage 비어있음: {packageKey}");
            return new List<ObjectPoolerManager.Pool>();
        }

        var pools = new List<ObjectPoolerManager.Pool>();
        var addedTags = new HashSet<string>();

        foreach (var info in package.Pools)
        {
            if (string.IsNullOrEmpty(info.Tag) || addedTags.Contains(info.Tag)) continue;

            // 2. AddressableManager를 통해 Prefab 로드
            GameObject prefab = await Managers.AddressableManager.LoadAssetAsync<GameObject>(info.ResourcePath);
            if (prefab == null)
            {
                Debug.LogWarning($"Prefab 누락: {info.Tag}");
                continue;
            }

            pools.Add(new ObjectPoolerManager.Pool
            {
                tag = info.Tag,
                prefab = prefab,
                initialSize = Mathf.Max(1, info.InitialSize),
                poolType = info.PoolType
            });

            addedTags.Add(info.Tag);
        }

        return pools;
    }
}
