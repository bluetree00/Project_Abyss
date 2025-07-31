using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

public static class ObjectPoolEffectInitializer
{
    public static async Task<List<ObjectPoolerManager.Pool>> GetInitialPoolsAsync(string poolKey)
    {
        var handle = Addressables.LoadAssetAsync<EffectPoolData>(poolKey);
        await handle.Task;

        if (handle.Status != AsyncOperationStatus.Succeeded)
        {
            Debug.LogError($"[GetInitialPoolsAsync] '{poolKey}' 로드 실패");
            return new List<ObjectPoolerManager.Pool>();
        }

        var effectPoolData = handle.Result;
        List<ObjectPoolerManager.Pool> pools = new List<ObjectPoolerManager.Pool>();

        foreach (var pool in effectPoolData.pools)
        {
            pools.Add(new ObjectPoolerManager.Pool
            {
                tag = pool.tag,
                resourcePath = pool.resourcePath,
                initialSize = pool.initialSize,
                poolType = pool.poolType
            });
        }

        return pools;
    }


}
