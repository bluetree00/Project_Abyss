using System.Collections.Generic;
using UnityEngine;

public static class ObjectPoolEffectInitializer
{
    public static List<ObjectPoolerManager.Pool> GetInitialPools(string effectPoolDataName)
    {
        // 스크립터블 오브젝트를 이름으로 로드
        EffectPoolData effectPoolData = Resources.Load<EffectPoolData>($"Data/{effectPoolDataName}");
        
        if (effectPoolData == null)
        {
            Debug.LogError($"EffectPoolData with name {effectPoolDataName} not found.");
            return new List<ObjectPoolerManager.Pool>();
        }

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
