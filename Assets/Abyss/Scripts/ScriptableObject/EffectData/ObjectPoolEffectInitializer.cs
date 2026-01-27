// using System;
// using System.Collections.Generic;
// using System.Threading.Tasks;
// using UnityEngine;
// using UnityEngine.AddressableAssets;
// using UnityEngine.ResourceManagement.AsyncOperations;

// /// <summary>
// /// ObjectPoolerManager 개선형 초기화 클래스
// /// Effect / Monster 등 타입별 풀 초기화를 Prefab 로드 기반으로 처리
// /// </summary>
// public static class ObjectPoolInitializer
// {
//     /// <summary>
//     /// Addressables 기반으로 EffectPoolData를 로드하고 Pool 리스트 생성
//     /// </summary>
//     public static async Task<List<ObjectPoolerManager.Pool>> GetEffectPoolsAsync(string effectPoolKey, int defaultSize = 5)
//     {
//         var handle = Addressables.LoadAssetAsync<EffectPoolData>(effectPoolKey);
//         await handle.Task;

//         if (handle.Status != AsyncOperationStatus.Succeeded)
//         {
//             Debug.LogError($"[GetEffectPoolsAsync] '{effectPoolKey}' 로드 실패");
//             return new List<ObjectPoolerManager.Pool>();
//         }

//         var effectPoolData = handle.Result;
//         List<ObjectPoolerManager.Pool> pools = new List<ObjectPoolerManager.Pool>();
//         HashSet<string> addedTags = new HashSet<string>();

//         foreach (var pool in effectPoolData.pools)
//         {
//             if (pool == null || string.IsNullOrEmpty(pool.tag) || addedTags.Contains(pool.tag))
//                 continue;

//             // Prefab을 비동기로 미리 로드
//             GameObject prefab = await LoadPrefabAsync(pool.resourcePath);
//             if (prefab == null)
//             {
//                 Debug.LogWarning($"Prefab 로드 실패: {pool.resourcePath}");
//                 continue;
//             }

//             pools.Add(new ObjectPoolerManager.Pool
//             {
//                 tag = pool.tag,
//                 prefab = prefab,          // 개선된 풀러는 prefab 직접 사용
//                 initialSize = Mathf.Max(1, pool.initialSize > 0 ? pool.initialSize : defaultSize),
//                 poolType = pool.poolType
//             });

//             addedTags.Add(pool.tag);
//         }

//         return pools;
//     }
// }