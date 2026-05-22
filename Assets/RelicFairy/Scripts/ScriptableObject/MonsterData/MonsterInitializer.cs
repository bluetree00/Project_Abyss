// using System.Collections.Generic;
// using UnityEngine;

// public static class MonsterInitializer
// {
//     public static List<ObjectPoolerManager.Pool> GetInitialPools(string monsterPoolDataName)
//     {
//         // 스크립터블 오브젝트를 이름으로 로드
//         MonsterPoolData monsterPoolData = Resources.Load<MonsterPoolData>($"Data/{monsterPoolDataName}");
        
//         if (monsterPoolData == null)
//         {
//             Debug.LogError($"EffectPoolData with name {monsterPoolDataName} not found.");
//             return new List<ObjectPoolerManager.Pool>();
//         }

//         List<ObjectPoolerManager.Pool> pools = new List<ObjectPoolerManager.Pool>();

//         foreach (var pool in monsterPoolData.pools)
//         {
//             pools.Add(new ObjectPoolerManager.Pool
//             {
//                 tag = pool.tag,
//                 resourcePath = pool.resourcePath,
//                 initialSize = pool.initialSize,
//                 poolType = pool.poolType
//             });
//         }

//         return pools;
//     }
// }
