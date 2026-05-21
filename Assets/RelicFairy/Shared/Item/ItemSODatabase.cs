using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 모든 ItemSO를 보유하는 데이터베이스 SO.
/// 부트스트래퍼에서 로드하여 ItemSORegistry에 등록.
/// Addressable로 로드하거나, Inspector에서 직접 할당.
/// </summary>
[CreateAssetMenu(menuName = "RelicFairy/Item/ItemSODatabase")]
public class ItemSODatabase : ScriptableObject
{
    [SerializeField] private List<ItemSO> items = new();

    public IReadOnlyList<ItemSO> Items => items;

    /// <summary>보유한 모든 SO를 레지스트리에 등록.</summary>
    public void RegisterAll()
    {
        ItemSORegistry.RegisterAll(items);
        Debug.Log($"[ItemSODatabase] {items.Count}개 ItemSO → Registry 등록");
    }
}
