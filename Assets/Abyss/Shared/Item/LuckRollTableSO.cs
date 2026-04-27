using System;
using UnityEngine;

/// <summary>
/// 행운력(Luck) 기반 아이템 등급 추첨 테이블.
/// - Luck → 행운치 레벨(LuckLevel) 변환 구간
/// - 행운치 레벨별 등급(Common/Rare/Epic/Legendary) 가중치
/// - 행운치 레벨별 결과창 드롭 발생률
///
/// 정적 데이터 전용. 런타임 상태 저장 금지.
/// 호출은 보통 <see cref="LuckRollService"/>를 경유한다.
/// </summary>
[CreateAssetMenu(fileName = "LuckRollTable", menuName = "Abyss/Item/Luck Roll Table")]
public class LuckRollTableSO : ScriptableObject
{
    // ── Constants ────────────────────────────────────────────────────────────────

    /// <summary>등급 단계 수 (Common, Rare, Epic, Legendary).</summary>
    private const int RarityCount = 4;

    /// <summary>Luck→LuckLevel 변환 구간 디폴트 개수 (LuckLevel 0~6).</summary>
    private const int DefaultLuckRangeCount = 7;

    /// <summary>LuckLevel별 가중치/드롭률 디폴트 개수 (LuckLevel 0~6).</summary>
    private const int DefaultLuckLevelCount = 7;

    /// <summary>가중치 합이 0 이하일 때 fallback (Common 100%).</summary>
    private const float MinValidWeightSum = 0.0001f;

    // ── Nested Types ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Luck 범위 → 행운치 레벨 변환 한 항목.
    /// 반개구간 [minLuck, maxLuck). 단, 마지막 구간은 폐구간으로 사용한다.
    /// </summary>
    [Serializable]
    private struct LuckRange
    {
        public int minLuck;
        public int maxLuck;
        public int luckLevel;
    }

    /// <summary>행운치 레벨별 등급 가중치 한 항목.</summary>
    [Serializable]
    private struct RarityWeights
    {
        public int luckLevel;
        public float commonWeight;
        public float rareWeight;
        public float epicWeight;
        public float legendaryWeight;
    }

    /// <summary>행운치 레벨별 결과창 드롭 발생률 한 항목.</summary>
    [Serializable]
    private struct DropChanceEntry
    {
        public int luckLevel;
        [Range(0f, 1f)] public float dropChance;
    }

    // ── [SerializeField] ─────────────────────────────────────────────────────────

    [Header("Luck → 행운치 레벨 변환 구간")]
    [Tooltip("Luck 값을 행운치 레벨로 매핑한다. [minLuck, maxLuck) 반개구간 (마지막 구간은 폐구간).")]
    [SerializeField]
    private LuckRange[] luckRanges = new LuckRange[DefaultLuckRangeCount]
    {
        new LuckRange { minLuck = 0,  maxLuck = 10,  luckLevel = 0 },
        new LuckRange { minLuck = 10, maxLuck = 25,  luckLevel = 1 },
        new LuckRange { minLuck = 25, maxLuck = 40,  luckLevel = 2 },
        new LuckRange { minLuck = 40, maxLuck = 55,  luckLevel = 3 },
        new LuckRange { minLuck = 55, maxLuck = 70,  luckLevel = 4 },
        new LuckRange { minLuck = 70, maxLuck = 85,  luckLevel = 5 },
        new LuckRange { minLuck = 85, maxLuck = 100, luckLevel = 6 },
    };

    [Header("행운치 레벨 → 등급별 가중치 (%)")]
    [Tooltip("LuckLevel별 Common/Rare/Epic/Legendary 가중치. 합이 100일 필요는 없으며 비율로 사용된다.")]
    [SerializeField]
    private RarityWeights[] rarityWeights = new RarityWeights[DefaultLuckLevelCount]
    {
        new RarityWeights { luckLevel = 0, commonWeight = 100f, rareWeight = 0f,  epicWeight = 0f,  legendaryWeight = 0f  },
        new RarityWeights { luckLevel = 1, commonWeight = 45f,  rareWeight = 33f, epicWeight = 20f, legendaryWeight = 2f  },
        new RarityWeights { luckLevel = 2, commonWeight = 30f,  rareWeight = 40f, epicWeight = 25f, legendaryWeight = 5f  },
        new RarityWeights { luckLevel = 3, commonWeight = 19f,  rareWeight = 30f, epicWeight = 41f, legendaryWeight = 10f },
        new RarityWeights { luckLevel = 4, commonWeight = 18f,  rareWeight = 25f, epicWeight = 33f, legendaryWeight = 24f },
        new RarityWeights { luckLevel = 5, commonWeight = 17f,  rareWeight = 20f, epicWeight = 29f, legendaryWeight = 34f },
        new RarityWeights { luckLevel = 6, commonWeight = 9f,   rareWeight = 17f, epicWeight = 34f, legendaryWeight = 40f },
    };

    [Header("행운치 레벨 → 결과창 드롭 발생률")]
    [Tooltip("결과창에서 아이템이 드롭될 확률 (0~1). 디폴트 0.6 (60%).")]
    [SerializeField]
    private DropChanceEntry[] dropChances = new DropChanceEntry[DefaultLuckLevelCount]
    {
        new DropChanceEntry { luckLevel = 0, dropChance = 0.6f },
        new DropChanceEntry { luckLevel = 1, dropChance = 0.6f },
        new DropChanceEntry { luckLevel = 2, dropChance = 0.6f },
        new DropChanceEntry { luckLevel = 3, dropChance = 0.6f },
        new DropChanceEntry { luckLevel = 4, dropChance = 0.6f },
        new DropChanceEntry { luckLevel = 5, dropChance = 0.6f },
        new DropChanceEntry { luckLevel = 6, dropChance = 0.6f },
    };

    // ── Private Fields (Cache) ───────────────────────────────────────────────────

    /// <summary>OnValidate/지연 빌드로 채워지는 캐시. 인덱스: 행운치 레벨.</summary>
    private System.Collections.Generic.Dictionary<int, RarityWeights> _weightsCache;
    private System.Collections.Generic.Dictionary<int, float> _dropCache;
    private bool _cacheBuilt;

    // ── Lifecycle ────────────────────────────────────────────────────────────────

    private void OnEnable()
    {
        _cacheBuilt = false;
    }

    private void OnValidate()
    {
        // Inspector 변경 시 캐시 무효화
        _cacheBuilt = false;
    }

    // ── Public Methods ───────────────────────────────────────────────────────────

    /// <summary>Luck 값을 행운치 레벨로 변환한다. 범위를 벗어나면 양 끝으로 클램프.</summary>
    public int GetLuckLevel(int luck)
    {
        if (luckRanges == null || luckRanges.Length == 0)
        {
            Debug.LogWarning("[LuckRollTableSO] luckRanges is empty. Returning 0.");
            return 0;
        }

        int lastIndex = luckRanges.Length - 1;

        // 첫 구간 미만 → 첫 구간 LuckLevel
        if (luck < luckRanges[0].minLuck)
            return luckRanges[0].luckLevel;

        // 마지막 구간 이상(폐구간) → 마지막 구간 LuckLevel
        if (luck >= luckRanges[lastIndex].maxLuck)
            return luckRanges[lastIndex].luckLevel;

        // 중간 구간: [minLuck, maxLuck) 반개구간
        for (int i = 0; i < luckRanges.Length; i++)
        {
            var range = luckRanges[i];
            if (i == lastIndex)
            {
                // 마지막은 폐구간
                if (luck >= range.minLuck && luck <= range.maxLuck)
                    return range.luckLevel;
            }
            else
            {
                if (luck >= range.minLuck && luck < range.maxLuck)
                    return range.luckLevel;
            }
        }

        // 구간 사이에 갭이 있을 경우 안전 fallback: 가장 가까운 하위 구간
        return luckRanges[0].luckLevel;
    }

    /// <summary>
    /// 행운치 레벨로 등급별 가중치를 조회한다.
    /// 등록되지 않은 레벨이면 가장 가까운 항목으로 클램프 (안전 fallback).
    /// </summary>
    public (float c, float r, float e, float l) GetWeights(int luckLevel)
    {
        EnsureCache();

        if (_weightsCache != null && _weightsCache.TryGetValue(luckLevel, out var w))
            return (w.commonWeight, w.rareWeight, w.epicWeight, w.legendaryWeight);

        // Fallback: 가장 가까운 LuckLevel
        var fallback = FindNearestWeights(luckLevel);
        if (fallback.HasValue)
        {
            var fw = fallback.Value;
            return (fw.commonWeight, fw.rareWeight, fw.epicWeight, fw.legendaryWeight);
        }

        // 완전 비어있음 → Common 100%
        Debug.LogWarning($"[LuckRollTableSO] rarityWeights is empty. Returning Common 100% for LuckLevel {luckLevel}.");
        return (1f, 0f, 0f, 0f);
    }

    /// <summary>행운치 레벨로 결과창 드롭 발생률을 조회한다. 등록되지 않은 레벨이면 가장 가까운 값.</summary>
    public float GetDropChance(int luckLevel)
    {
        EnsureCache();

        if (_dropCache != null && _dropCache.TryGetValue(luckLevel, out var chance))
            return chance;

        // Fallback: 가장 가까운 LuckLevel
        if (dropChances == null || dropChances.Length == 0)
            return 0f;

        return FindNearestDropChance(luckLevel);
    }

    // ── Private Methods ──────────────────────────────────────────────────────────

    private void EnsureCache()
    {
        if (_cacheBuilt) return;

        _weightsCache = new System.Collections.Generic.Dictionary<int, RarityWeights>();
        if (rarityWeights != null)
        {
            foreach (var w in rarityWeights)
            {
                _weightsCache[w.luckLevel] = w;
            }
        }

        _dropCache = new System.Collections.Generic.Dictionary<int, float>();
        if (dropChances != null)
        {
            foreach (var d in dropChances)
            {
                _dropCache[d.luckLevel] = Mathf.Clamp01(d.dropChance);
            }
        }

        _cacheBuilt = true;
    }

    /// <summary>요청 LuckLevel과 가장 가까운 가중치 항목을 찾는다.</summary>
    private RarityWeights? FindNearestWeights(int luckLevel)
    {
        if (rarityWeights == null || rarityWeights.Length == 0) return null;

        RarityWeights best = rarityWeights[0];
        int bestDist = Mathf.Abs(best.luckLevel - luckLevel);

        for (int i = 1; i < rarityWeights.Length; i++)
        {
            int dist = Mathf.Abs(rarityWeights[i].luckLevel - luckLevel);
            if (dist < bestDist)
            {
                bestDist = dist;
                best = rarityWeights[i];
            }
        }
        return best;
    }

    /// <summary>요청 LuckLevel과 가장 가까운 드롭 확률을 찾는다.</summary>
    private float FindNearestDropChance(int luckLevel)
    {
        DropChanceEntry best = dropChances[0];
        int bestDist = Mathf.Abs(best.luckLevel - luckLevel);

        for (int i = 1; i < dropChances.Length; i++)
        {
            int dist = Mathf.Abs(dropChances[i].luckLevel - luckLevel);
            if (dist < bestDist)
            {
                bestDist = dist;
                best = dropChances[i];
            }
        }
        return Mathf.Clamp01(best.dropChance);
    }

    // ── Internal Helpers (LuckRollService 전용) ─────────────────────────────────

    /// <summary>가중치 합이 유효한지 검사. 0 이하이면 false.</summary>
    internal static bool IsWeightSumValid(float sum) => sum > MinValidWeightSum;

    // ── Editor Sanity Check ──────────────────────────────────────────────────────

#if UNITY_EDITOR
    /// <summary>Editor 인스펙터에서 우클릭 → 1000회 시뮬레이션 후 분포 출력.</summary>
    [ContextMenu("Sanity Check / Roll 1000x at Luck=50")]
    private void EditorSanityCheckLuck50() => RunSanityCheck(50, 1000);

    [ContextMenu("Sanity Check / Roll 1000x at Luck=10")]
    private void EditorSanityCheckLuck10() => RunSanityCheck(10, 1000);

    [ContextMenu("Sanity Check / Roll 1000x at Luck=100")]
    private void EditorSanityCheckLuck100() => RunSanityCheck(100, 1000);

    private void RunSanityCheck(int luck, int trials)
    {
        EnsureCache();
        int level = GetLuckLevel(luck);
        var (cW, rW, eW, lW) = GetWeights(level);
        float sum = cW + rW + eW + lW;

        int[] counts = new int[RarityCount];
        for (int i = 0; i < trials; i++)
        {
            var rarity = LuckRollService.RollRarity(luck, this);
            counts[(int)rarity]++;
        }

        float pct(int n) => trials > 0 ? n * 100f / trials : 0f;
        Debug.Log(
            $"[LuckRollTableSO] SanityCheck Luck={luck} → LuckLevel={level} (trials={trials})\n" +
            $"  Expected (sum={sum}): C={cW / sum * 100f:F1}%, R={rW / sum * 100f:F1}%, E={eW / sum * 100f:F1}%, L={lW / sum * 100f:F1}%\n" +
            $"  Observed: C={pct(counts[0]):F1}% ({counts[0]}), R={pct(counts[1]):F1}% ({counts[1]}), E={pct(counts[2]):F1}% ({counts[2]}), L={pct(counts[3]):F1}% ({counts[3]})\n" +
            $"  DropChance(LuckLevel={level}) = {GetDropChance(level):F2}");
    }
#endif
}
