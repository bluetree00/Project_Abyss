using System;
using System.Collections.Generic;
using UnityEngine;

public static class ObjectPoolEffectInitializer
{
    public static void GetInitialPools(string effectPoolDataName, Action<List<ObjectPoolerManager.Pool>> callback)
    {
        // AddressablesManager를 사용하여 Addressables로 풀 데이터를 로드합니다.
        //[ ]
        Managers.AddressableManager.LoadAsset<EffectPoolData>(effectPoolDataName, effectPoolData =>
        {
            if (effectPoolData == null)
            {
                Debug.LogError($"EffectPoolData with name {effectPoolDataName} not found.");
                callback(new List<ObjectPoolerManager.Pool>());
                return;
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

            callback(pools);
        });
    }
}
