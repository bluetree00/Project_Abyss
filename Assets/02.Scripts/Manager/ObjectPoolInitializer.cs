using System.Collections.Generic;
using UnityEngine;

public static class ObjectPoolInitializer
{
    public static List<ObjectPoolerManager.Pool> GetInitialPools()
    {
        return new List<ObjectPoolerManager.Pool>
        {
            new ObjectPoolerManager.Pool { tag = "ShinySlash", resourcePath = "Effects/ShinySlash", initialSize = 10 },
            new ObjectPoolerManager.Pool { tag = "HitEffect_02", resourcePath = "Effects/HitEffect_02", initialSize = 10 },
            new ObjectPoolerManager.Pool { tag = "DieEffect_01", resourcePath = "Effects/DieEffect_01", initialSize = 5 }
        };
    }
}
