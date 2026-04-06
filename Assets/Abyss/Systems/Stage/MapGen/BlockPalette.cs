using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 테마별 블록 세트. TileType → BlockDef 매핑.
/// 같은 TileType에 여러 BlockDef를 등록하면 가중치 랜덤 선택.
/// </summary>
[CreateAssetMenu(menuName = "Map/BlockPalette")]
public class BlockPalette : ScriptableObject
{
    [SerializeField] private List<BlockDef> blocks = new();

    private Dictionary<TileType, List<BlockDef>> _cache;

    /// <summary>TileType에 맞는 BlockDef를 가중치 랜덤으로 선택.</summary>
    public BlockDef Pick(TileType type)
    {
        BuildCacheIfNeeded();

        if (!_cache.TryGetValue(type, out var list) || list.Count == 0)
            return null;

        if (list.Count == 1)
            return list[0];

        // 가중치 랜덤
        int total = 0;
        foreach (var b in list) total += b.weight;

        int roll = Random.Range(0, total);
        int acc = 0;
        foreach (var b in list)
        {
            acc += b.weight;
            if (roll < acc) return b;
        }

        return list[0];
    }

    /// <summary>특정 TileType에 등록된 BlockDef가 있는지.</summary>
    public bool Has(TileType type)
    {
        BuildCacheIfNeeded();
        return _cache.ContainsKey(type) && _cache[type].Count > 0;
    }

    private void BuildCacheIfNeeded()
    {
        if (_cache != null) return;

        _cache = new Dictionary<TileType, List<BlockDef>>();
        foreach (var b in blocks)
        {
            if (b == null) continue;
            if (!_cache.TryGetValue(b.tileType, out var list))
            {
                list = new List<BlockDef>();
                _cache[b.tileType] = list;
            }
            list.Add(b);
        }
    }

    private void OnValidate()
    {
        _cache = null; // 에디터 변경 시 캐시 리셋
    }
}
