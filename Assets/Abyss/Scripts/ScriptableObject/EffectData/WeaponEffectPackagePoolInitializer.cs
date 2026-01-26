using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

public static class WeaponEffectPackagePoolInitializer
{
    /// <summary>
    /// SO 패키지를 보고 풀 정보를 만들고 Prefab을 미리 로드해서 ObjectPoolerManager용 Pool 리스트 반환
    /// </summary>
    public static async Task<List<ObjectPoolerManager.Pool>> GetPoolsAsync(
        WeaponEffectPackageSO package, int defaultPoolSize = 5)
    {
        if (package == null) return new List<ObjectPoolerManager.Pool>();

        List<ObjectPoolerManager.Pool> pools = new List<ObjectPoolerManager.Pool>();
        HashSet<string> addedTags = new HashSet<string>();

        foreach (var action in package.actions)
        {
            foreach (var effectSO in action.effects)
            {
                if (effectSO == null || string.IsNullOrEmpty(effectSO.prefabKey))
                    continue;

                // 이미 처리한 태그면 스킵
                if (addedTags.Contains(effectSO.prefabKey))
                    continue;

                // 🔹 Addressables에서 Prefab 미리 로드
                GameObject prefab = await LoadPrefabAsync(effectSO.prefabKey);
                if (prefab == null)
                {
                    Debug.LogWarning($"Failed to load prefab for key: {effectSO.prefabKey}");
                    continue;
                }

                // 🔹 Pool 객체 생성
                pools.Add(new ObjectPoolerManager.Pool
                {
                    tag = effectSO.prefabKey,
                    prefab = prefab, // 개선된 풀러는 미리 로드한 Prefab 참조 사용
                    initialSize = defaultPoolSize,
                    poolType = ObjectPoolerManager.PoolType.Effect
                });

                addedTags.Add(effectSO.prefabKey);
            }
        }

        return pools;
    }

    /// <summary>
    /// Addressables에서 Prefab을 비동기로 로드
    /// </summary>
    private static async Task<GameObject> LoadPrefabAsync(string key)
    {
        if (string.IsNullOrEmpty(key)) return null;

        AsyncOperationHandle<GameObject> handle = Addressables.LoadAssetAsync<GameObject>(key);
        await handle.Task;

        if (handle.Status == AsyncOperationStatus.Succeeded)
            return handle.Result;

        return null;
    }
}
