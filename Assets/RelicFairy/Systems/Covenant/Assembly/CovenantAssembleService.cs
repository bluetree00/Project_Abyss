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
    private const double GoldCut = 0.60;
    private const double RubyCut = 0.90;

    // 연마 확률(실버 50 / 골드 35 / 루비 15) — 런당 1회뿐이라 일반 굴림보다 후하게.
    private const double WhetGoldCut = 0.50;
    private const double WhetRubyCut = 0.85;

    public static List<CovenantDraftCard> DraftCauses(int count, System.Random rng, bool forceSilver)
        => DraftCards(CovenantPalette.CauseIds, count, rng, forceSilver);

    /// <summary>
    /// 효과 카드 드래프트. 3장 중 최소 1장은 방어축(Survival)을 보장한다 —
    /// 공격 효과만 뜨는 판이 반복되면 플레이어에게 남는 선택은 "얼마나 세게 때릴까"뿐이고,
    /// 생존이 필요한 순간에는 서약이 아무 대답도 못 한다.
    /// </summary>
    public static List<CovenantDraftCard> DraftEffects(int count, System.Random rng, bool forceSilver)
    {
        var cards = DraftCards(CovenantPalette.EffectIds, count, rng, forceSilver);
        EnsureSurvival(cards, rng);
        return cards;
    }

    /// <summary>제외 목록에 없는 새 카드 1장(개별 리롤, 티어 재굴림). 후보 없으면 null.</summary>
    public static CovenantDraftCard? RerollCard(IReadOnlyList<string> pool, ICollection<string> excludeIds,
                                                System.Random rng, bool forceSilver)
    {
        var id = RerollOne(pool, excludeIds, rng, requireSurvival: false);
        if (id == null) return null;
        return new CovenantDraftCard(id, RollTier(rng, forceSilver));
    }

    /// <summary>
    /// 효과 카드 개별 리롤. 방어축이 그 한 장뿐이면 방어축으로만 교체된다(axisLock) —
    /// 보장을 리롤 한 번으로 우회할 수 있으면 보장이 아니다.
    /// </summary>
    public static CovenantDraftCard? RerollEffectCard(IReadOnlyList<CovenantDraftCard> current, int idx,
                                                      System.Random rng, bool forceSilver)
    {
        if (current == null || idx < 0 || idx >= current.Count) return null;

        var exclude = new HashSet<string>();
        int survivals = 0;
        for (int i = 0; i < current.Count; i++)
        {
            exclude.Add(current[i].id);
            if (CovenantPalette.IsSurvivalEffect(current[i].id)) survivals++;
        }

        bool mustSurvival = survivals <= 1 && CovenantPalette.IsSurvivalEffect(current[idx].id);
        var id = RerollOne(CovenantPalette.EffectIds, exclude, rng, mustSurvival);
        if (id == null) return null;
        return new CovenantDraftCard(id, RollTier(rng, forceSilver));
    }

    /// <summary>티어 1회 굴림. forceSilver면 무조건 실버.</summary>
    public static CovenantTier RollTier(System.Random rng, bool forceSilver)
        => RollTier(rng, forceSilver, GoldCut, RubyCut);

    /// <summary>연마 — id는 그대로 두고 티어만 다시 굴린다(실버 50 / 골드 35 / 루비 15).</summary>
    public static CovenantTier RollWhetTier(System.Random rng, bool forceSilver)
        => RollTier(rng, forceSilver, WhetGoldCut, WhetRubyCut);

    // ── 내부 ─────────────────────────────────────────────
    private static CovenantTier RollTier(System.Random rng, bool forceSilver, double goldCut, double rubyCut)
    {
        if (forceSilver) return CovenantTier.Silver;
        double r = rng != null ? rng.NextDouble() : UnityEngine.Random.value;
        if (r < goldCut) return CovenantTier.Silver;
        if (r < rubyCut) return CovenantTier.Gold;
        return CovenantTier.Ruby;
    }

    /// <summary>방어축이 한 장도 없으면 아무 한 칸을 방어축 효과로 바꾼다(티어는 굴린 그대로 유지).</summary>
    private static void EnsureSurvival(List<CovenantDraftCard> cards, System.Random rng)
    {
        if (cards == null || cards.Count == 0) return;
        for (int i = 0; i < cards.Count; i++)
            if (CovenantPalette.IsSurvivalEffect(cards[i].id)) return;

        var pool = new List<string>();
        foreach (var id in CovenantPalette.EffectIds)
            if (CovenantPalette.IsSurvivalEffect(id)) pool.Add(id);
        if (pool.Count == 0) return;

        int slot = Next(rng, cards.Count);
        int pick = Next(rng, pool.Count);
        cards[slot] = new CovenantDraftCard(pool[pick], cards[slot].tier);
    }

    private static string RerollOne(IReadOnlyList<string> pool, ICollection<string> exclude,
                                    System.Random rng, bool requireSurvival)
    {
        if (pool == null) return null;
        var candidates = new List<string>();
        foreach (var id in pool)
        {
            if (exclude != null && exclude.Contains(id)) continue;
            if (requireSurvival && !CovenantPalette.IsSurvivalEffect(id)) continue;
            candidates.Add(id);
        }
        if (candidates.Count == 0) return null;
        return candidates[Next(rng, candidates.Count)];
    }

    private static List<CovenantDraftCard> DraftCards(IReadOnlyList<string> pool, int count,
                                                      System.Random rng, bool forceSilver)
    {
        var ids = Draft(pool, count, rng);
        var cards = new List<CovenantDraftCard>(ids.Count);
        foreach (var id in ids)
            cards.Add(new CovenantDraftCard(id, RollTier(rng, forceSilver)));
        return cards;
    }

    private static List<string> Draft(IReadOnlyList<string> pool, int count, System.Random rng)
    {
        var bag = new List<string>(pool);
        for (int i = bag.Count - 1; i > 0; i--)   // Fisher-Yates
        {
            int j = Next(rng, i + 1);
            (bag[i], bag[j]) = (bag[j], bag[i]);
        }
        if (count < bag.Count) bag.RemoveRange(count, bag.Count - count);
        return bag;
    }

    private static int Next(System.Random rng, int exclusiveMax)
        => rng != null ? rng.Next(exclusiveMax) : UnityEngine.Random.Range(0, exclusiveMax);
}
