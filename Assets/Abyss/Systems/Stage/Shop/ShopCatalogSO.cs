using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 상점 방 1개가 참조하는 상품 풀.
/// 방 진입 시 slotCount만큼 가중치 기반 중복 없이 추출.
/// </summary>
[CreateAssetMenu(menuName = "Shop/ShopCatalog", fileName = "ShopCatalog_")]
public class ShopCatalogSO : ScriptableObject
{
    [SerializeField] private List<ShopItemSO> entries = new();

    public IReadOnlyList<ShopItemSO> Entries => entries;

    /// <summary>가중치 기반 중복 없는 랜덤 추출. 엔트리 수가 count보다 적으면 가능한 만큼만 반환.</summary>
    public List<ShopItemSO> PickRandom(int count)
    {
        var result = new List<ShopItemSO>(count);
        if (count <= 0 || entries == null || entries.Count == 0)
            return result;

        // 유효 후보만 복사 (item 할당 + weight > 0)
        var pool = new List<ShopItemSO>(entries.Count);
        foreach (var e in entries)
        {
            if (e == null || e.Item == null || e.Weight <= 0) continue;
            pool.Add(e);
        }

        int take = Mathf.Min(count, pool.Count);
        for (int i = 0; i < take; i++)
        {
            int total = 0;
            for (int j = 0; j < pool.Count; j++) total += pool[j].Weight;
            if (total <= 0) break;

            int roll = Random.Range(0, total);
            int acc = 0;
            int pickIdx = pool.Count - 1;
            for (int j = 0; j < pool.Count; j++)
            {
                acc += pool[j].Weight;
                if (roll < acc) { pickIdx = j; break; }
            }

            result.Add(pool[pickIdx]);
            pool.RemoveAt(pickIdx);
        }

        return result;
    }
}
