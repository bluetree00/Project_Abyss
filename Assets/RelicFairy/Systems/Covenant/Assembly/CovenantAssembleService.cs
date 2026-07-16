using System.Collections.Generic;

/// <summary>드래프트로 제시되는 카드 1장 — 원인/효과 id + 굴린 티어.</summary>
public readonly struct CovenantDraftCard
{
    public readonly string id;
    public readonly CovenantTier tier;
    public CovenantDraftCard(string id, CovenantTier tier) { this.id = id; this.tier = tier; }
}

/// <summary>
/// 조립 서약 드래프트/리롤 서비스. 팔레트에서 원인·효과를 무작위 제시(티어 포함)하고, 리롤로 교체.
/// UI(UI_CovenantAssemble)가 사용. 결정성 필요 시 seed 있는 System.Random 전달(save-scum 방지).
/// forceSilver=true(첫 서약)면 모든 카드 티어를 실버로 고정.
/// </summary>
public static class CovenantAssembleService
{
    // 티어 드래프트 확률(실버 60 / 골드 30 / 루비 10). 밸런스 시작점.
    private const double GoldCut  = 0.60;
    private const double RubyCut = 0.90;

    public static List<CovenantDraftCard> DraftCauses(int count, System.Random rng, bool forceSilver)
        => DraftCards(CovenantPalette.CauseIds, count, rng, forceSilver);

    public static List<CovenantDraftCard> DraftEffects(int count, System.Random rng, bool forceSilver)
        => DraftCards(CovenantPalette.EffectIds, count, rng, forceSilver);

    /// <summary>제외 목록에 없는 새 카드 1장(개별 리롤, 티어 재굴림). 후보 없으면 null.</summary>
    public static CovenantDraftCard? RerollCard(IReadOnlyList<string> pool, ICollection<string> excludeIds,
                                                System.Random rng, bool forceSilver)
    {
        var id = RerollOne(pool, excludeIds, rng);
        if (id == null) return null;
        return new CovenantDraftCard(id, RollTier(rng, forceSilver));
    }

    /// <summary>티어 1회 굴림. forceSilver면 무조건 실버.</summary>
    public static CovenantTier RollTier(System.Random rng, bool forceSilver)
    {
        if (forceSilver) return CovenantTier.Silver;
        double r = rng != null ? rng.NextDouble() : UnityEngine.Random.value;
        if (r < GoldCut)  return CovenantTier.Silver;
        if (r < RubyCut) return CovenantTier.Gold;
        return CovenantTier.Ruby;
    }

    // ── 내부 ─────────────────────────────────────────────
    private static List<CovenantDraftCard> DraftCards(IReadOnlyList<string> pool, int count,
                                                      System.Random rng, bool forceSilver)
    {
        var ids = Draft(pool, count, rng);
        var cards = new List<CovenantDraftCard>(ids.Count);
        foreach (var id in ids)
            cards.Add(new CovenantDraftCard(id, RollTier(rng, forceSilver)));
        return cards;
    }

    private static string RerollOne(IReadOnlyList<string> pool, ICollection<string> exclude, System.Random rng)
    {
        if (pool == null) return null;
        var candidates = new List<string>();
        foreach (var id in pool)
            if (exclude == null || !exclude.Contains(id)) candidates.Add(id);
        if (candidates.Count == 0) return null;
        int i = rng != null ? rng.Next(candidates.Count) : UnityEngine.Random.Range(0, candidates.Count);
        return candidates[i];
    }

    private static List<string> Draft(IReadOnlyList<string> pool, int count, System.Random rng)
    {
        var bag = new List<string>(pool);
        for (int i = bag.Count - 1; i > 0; i--)   // Fisher-Yates
        {
            int j = rng != null ? rng.Next(i + 1) : UnityEngine.Random.Range(0, i + 1);
            (bag[i], bag[j]) = (bag[j], bag[i]);
        }
        if (count < bag.Count) bag.RemoveRange(count, bag.Count - count);
        return bag;
    }
}
