using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

// 초기화 클래스
public static class WeaponEffectPackagePoolInitializer
{
    public static async Task<List<ObjectPoolerManager.Pool>> GetPoolsAsync(
        WeaponEffectPackageSO package, int defaultPoolSize = 5)
    {
        if (package == null) return new List<ObjectPoolerManager.Pool>();

        List<ObjectPoolerManager.Pool> pools = new List<ObjectPoolerManager.Pool>();
        HashSet<string> addedTags = new HashSet<string>();

        foreach (var action in package.actions)
            foreach (var indexEntry in action.effectIndices)
                foreach (var step in indexEntry.steps)
                {
                    var effectSO = step.effectSO;
                    if (effectSO == null || string.IsNullOrEmpty(effectSO.prefabKey)) continue;
                    if (addedTags.Contains(effectSO.prefabKey)) continue;

                    pools.Add(new ObjectPoolerManager.Pool
                    {
                        tag = effectSO.prefabKey,
                        resourcePath = effectSO.prefabKey,
                        initialSize = defaultPoolSize,
                        poolType = ObjectPoolerManager.PoolType.Effect
                    });
                    addedTags.Add(effectSO.prefabKey);
                }

        await Task.Yield();
        return pools;
    }
}
