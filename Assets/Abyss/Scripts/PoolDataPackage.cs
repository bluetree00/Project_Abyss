using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 풀 초기화 데이터 패키지 (풀러용, 전부 Addressables)
/// </summary>
[CreateAssetMenu(fileName = "NewPoolDataPackage", menuName = "Pools/PoolDataPackage")]
public class PoolDataPackage : ScriptableObject
{
    [Header("Addressables Key")]
    [Tooltip("풀 패키지 SO를 Addressables에서 로드할 때 사용하는 키")]
    public string KeyOrName;

    [Header("Pool Info")]
    [Tooltip("각 풀의 정보 리스트")]
    public List<PoolInfo> Pools = new List<PoolInfo>();
}

/// <summary>
/// 풀 정보
/// </summary>
[System.Serializable]
public class PoolInfo
{
    public string Tag;
    public string ResourcePath;    // Addressables 키
    public int InitialSize;
    public ObjectPoolerManager.PoolType PoolType; // Effect / GameObject 등
}
