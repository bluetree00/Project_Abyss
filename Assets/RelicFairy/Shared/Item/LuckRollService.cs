using UnityEngine;

/// <summary>
/// 행운력(Luck) 기반 아이템 등급/드롭 추첨 정적 서비스.
/// 의존성 없음 — <see cref="LuckRollTableSO"/>만 받아 처리한다.
///
/// Phase 1: 데이터/서비스만 제공. 호출처(상점/드롭/결과창)는 Phase 2에서 연결.
/// </summary>
public static class LuckRollService
{
    // ── Constants ────────────────────────────────────────────────────────────────

    /// <summary>테이블이 null일 때 안전 fallback 등급.</summary>
    private const ItemRarity FallbackRarity = ItemRarity.Common;

    /// <summary>테이블이 null일 때 안전 fallback 행운치 레벨.</summary>
    private const int FallbackLuckLevel = 0;

    /// <summary>테이블이 null일 때 안전 fallback 드롭 확률.</summary>
    private const float FallbackDropChance = 0f;

    // ── Public Methods ───────────────────────────────────────────────────────────

    /// <summary>Luck 값을 행운치 레벨로 변환한다. 테이블이 null이면 0 반환.</summary>
    public static int GetLuckLevel(int luck, LuckRollTableSO table)
    {
        if (table == null)
        {
            Debug.LogWarning("[LuckRollService] LuckRollTableSO is null. Returning fallback LuckLevel 0.");
            return FallbackLuckLevel;
        }
        return table.GetLuckLevel(luck);
    }

    /// <summary>
    /// Luck → 행운치 레벨 → 등급 가중치 → 가중 추첨으로 등급을 결정한다.
    /// 가중치 합이 0 이하이면 Common 반환.
    /// <paramref name="rng"/>가 주어지면(방 시드 기반 결정적 RNG) 그 소스로 추첨하여
    /// 같은 시드면 항상 같은 등급이 나오도록 한다(이어하기 결정성). null이면 전역 UnityEngine.Random.
    /// </summary>
    public static ItemRarity RollRarity(int luck, LuckRollTableSO table, System.Random rng = null)
    {
        if (table == null)
        {
            Debug.LogWarning("[LuckRollService] LuckRollTableSO is null. Returning fallback Common.");
            return FallbackRarity;
        }

        int level = table.GetLuckLevel(luck);
        var (cW, rW, eW, lW) = table.GetWeights(level);

        // 음수 방어
        cW = Mathf.Max(0f, cW);
        rW = Mathf.Max(0f, rW);
        eW = Mathf.Max(0f, eW);
        lW = Mathf.Max(0f, lW);

        float sum = cW + rW + eW + lW;
        if (!LuckRollTableSO.IsWeightSumValid(sum))
        {
            Debug.LogWarning($"[LuckRollService] Total weight is zero for LuckLevel {level}. Falling back to Common.");
            return ItemRarity.Common;
        }

        float roll = rng != null ? (float)(rng.NextDouble() * sum) : Random.Range(0f, sum);

        // 누적 비교
        float cumulative = cW;
        if (roll < cumulative) return ItemRarity.Common;

        cumulative += rW;
        if (roll < cumulative) return ItemRarity.Rare;

        // 상위 등급은 기억의 제단에서 열려야 실제로 나온다 — 미해금이면 하위로 강등(정본 §4).
        cumulative += eW;
        if (roll < cumulative) return MemoryAltarService.ClampRarity(ItemRarity.Epic);

        return MemoryAltarService.ClampRarity(ItemRarity.Legendary);
    }

    /// <summary>Luck 기반 결과창 드롭 발생 확률(0~1) 조회.</summary>
    public static float GetDropChance(int luck, LuckRollTableSO table)
    {
        if (table == null)
        {
            Debug.LogWarning("[LuckRollService] LuckRollTableSO is null. Returning drop chance 0.");
            return FallbackDropChance;
        }

        int level = table.GetLuckLevel(luck);
        return table.GetDropChance(level);
    }

    /// <summary>드롭 확률에 따른 발생 여부 추첨. <c>Random.value &lt; chance</c>.</summary>
    public static bool TryRollDrop(int luck, LuckRollTableSO table)
    {
        float chance = GetDropChance(luck, table);
        if (chance <= 0f) return false;
        if (chance >= 1f) return true;
        return Random.value < chance;
    }
}
