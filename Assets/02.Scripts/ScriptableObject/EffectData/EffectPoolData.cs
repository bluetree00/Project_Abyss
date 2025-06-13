using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "NewEffectPoolData", menuName = "Pools/EffectPoolData")]
public class EffectPoolData : ScriptableObject
{
    [Tooltip("List of object pools with their respective settings.")]
    public List<PoolSettings> pools; // 풀 목록을 담는 리스트

    [System.Serializable]
    public class PoolSettings
    {
        [Tooltip("Unique identifier tag for this pool.")]
        public string tag; // 풀의 태그

        [Tooltip("Path to the prefab under Resources/Prefabs folder.")]
        public string resourcePath; // 리소스 경로

        [Tooltip("Initial number of objects to instantiate for this pool.")]
        public int initialSize; // 초기 오브젝트 수

        [Tooltip("Type of pool, used to assign to specific categories such as effects, monsters, etc.")]
        public ObjectPoolerManager.PoolType poolType; // 풀 타입
    }
}
