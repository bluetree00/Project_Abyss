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

    // 교체 확률(실버 50 / 골드 35 / 루비 15) — 처음 배분보다 후하다.
    //
    // <b>교체가 등급의 기회이기도 하다</b>: 카드를 바꾸는 것은 "이 효과가 싫다"이지만,
    // 실버가 떠서 아쉬운 카드를 넘길 때도 쓰인다. 첫 배분과 같은 확률이면 교체가
    // 순수 손실 위험이라 아무도 안 굴린다 — 후하게 줘야 굴릴 이유가 생긴다.
    // (연마는 폐기했다. 재굴림이 둘이면 규칙을 두 번 배워야 하는데, 등급만 다시 굴리는 쪽은
    //  교체의 하위 호환이라 값을 못 했다. 그 확률을 여기로 흡수했다.)
    private const double RerollGoldCut = 0.50;
    private const double RerollRubyCut = 0.85;

    /// <summary>
    /// 기억의 제단 「서약 카드」 개방 단계. 드래프트·교체·보장이 모두 이 단계까지 열린 카드에서만 뽑는다 —
    /// 한 곳이라도 전체 목록을 보면 교체 한 번으로 잠긴 카드를 끌어올 수 있다.
    /// </summary>
    private static int Step => _stepOverride ?? MemoryAltarService.CovenantPartStep;

    // 검증 도구(에디터 「서약 뽑기 전수 검증」)가 리플렉션으로 넣는 개방 단계. 게임 코드는 쓰지 않는다.
    private static int? _stepOverride;

    // 원인을 다시 뽑는 최대 횟수 — 제시한 원인 모두와 맞는 효과가 3장이 안 될 때만 돈다.
    private const int MaxBoardAttempts = 8;

    /// <summary>
    /// 원인·효과 카드를 함께 뽑는다. 제시되는 원인 × 효과 <b>모든 조합</b>이 벼릴 수 있는 짝이 되게 고른다
    /// (<see cref="CovenantPalette.CanPair"/>) — 고를 수 없는 칸을 보여 주면 그 칸이 낭비된다.
    /// 원인을 먼저 뽑고, 그 원인들 모두와 맞는 효과만 후보로 둔다. 후보가 모자라거나
    /// 생존 카드가 후보에서 모두 빠지면(봉인 짝·보유 짝 때문에) 원인을 다시 뽑는다.
    ///
    /// 효과 3장 중 최소 1장은 방어축(Survival)을 보장한다 — 공격 효과만 뜨는 판이 반복되면
    /// 생존이 필요한 순간에 서약이 아무 대답도 못 한다.
    /// held(보유 서약)는 <b>페어링</b>에도 쓰인다(C4) — 소모형은 그 통화를 걸어 줄 서약이 있을 때만 나온다.
    /// </summary>
    public static void DraftBoard(int count, System.Random rng, bool forceSilver, IReadOnlyList<CovenantBase> held,
                                  out List<CovenantDraftCard> causes, out List<CovenantDraftCard> effects,
                                  ICollection<StatusCurrency> build = null)
    {
        var causePool  = CovenantPalette.DraftableCauseIds(Step);
        var effectBase = CovenantPalette.DraftableEffectIds(held, Step, build);
        causes  = null;
        effects = null;
        for (int attempt = 0; attempt < MaxBoardAttempts; attempt++)
        {
            causes = DraftCards(causePool, count, rng, forceSilver);
            var pool = PairableEffects(effectBase, causes, held);
            bool shortfall = pool.Count < count || (HasGuaranteedSurvival(effectBase) && !HasGuaranteedSurvival(pool));
            if (shortfall && attempt < MaxBoardAttempts - 1) continue;

            effects = DraftCards(pool, count, rng, forceSilver);
            EnsureSurvival(effects, pool, rng);
            return;
        }
    }

    /// <summary>
    /// 원인 카드 개별 교체 — 개방 단계까지 열린 원인 중, 지금 제시된 효과 카드 전부와 짝이 되는 것만 고른다.
    /// 후보 없으면 null.
    /// </summary>
    public static CovenantDraftCard? RerollCauseCard(ICollection<string> excludeIds, System.Random rng, bool forceSilver,
                                                     IReadOnlyList<CovenantDraftCard> effects,
                                                     IReadOnlyList<CovenantBase> held)
    {
        var pool = new List<string>();
        foreach (var id in CovenantPalette.DraftableCauseIds(Step))
            if (PairsWithAll(id, effects, held, isCause: true)) pool.Add(id);
        return RerollCard(pool, excludeIds, rng, forceSilver);
    }

    /// <summary>제외 목록에 없는 새 카드 1장(개별 리롤, 티어 재굴림). 후보 없으면 null.</summary>
    public static CovenantDraftCard? RerollCard(IReadOnlyList<string> pool, ICollection<string> excludeIds,
                                                System.Random rng, bool forceSilver)
    {
        var id = RerollOne(pool, excludeIds, rng, requireSurvival: false);
        if (id == null) return null;
        return new CovenantDraftCard(id, RollRerollTier(rng, forceSilver));
    }

    /// <summary>
    /// 효과 카드 개별 리롤. 방어축이 그 한 장뿐이면 방어축으로만 교체된다(axisLock) —
    /// 보장을 리롤 한 번으로 우회할 수 있으면 보장이 아니다.
    /// 후보 풀도 드래프트와 같은 페어링 규칙(C4)과 짝 규칙(제시된 원인 전부와 짝)을 따른다 —
    /// 리롤로 규칙 밖의 카드를 끌어올 수 있으면 규칙이 아니다.
    /// </summary>
    public static CovenantDraftCard? RerollEffectCard(IReadOnlyList<CovenantDraftCard> current, int idx,
                                                      System.Random rng, bool forceSilver,
                                                      IReadOnlyList<CovenantBase> held,
                                                      IReadOnlyList<CovenantDraftCard> causes,
                                                      ICollection<StatusCurrency> build = null)
    {
        if (current == null || idx < 0 || idx >= current.Count) return null;

        var exclude = new HashSet<string>();
        int guaranteed = 0;
        for (int i = 0; i < current.Count; i++)
        {
            exclude.Add(current[i].id);
            if (CovenantPalette.IsGuaranteedSurvivalEffect(current[i].id)) guaranteed++;
        }

        bool mustSurvival = guaranteed <= 1 && CovenantPalette.IsGuaranteedSurvivalEffect(current[idx].id);
        var pool = PairableEffects(CovenantPalette.DraftableEffectIds(held, Step, build), causes, held);
        var id = RerollOne(pool, exclude, rng, mustSurvival);
        if (id == null) return null;
        return new CovenantDraftCard(id, RollRerollTier(rng, forceSilver));
    }

    /// <summary>티어 1회 굴림. forceSilver면 무조건 실버.</summary>
    public static CovenantTier RollTier(System.Random rng, bool forceSilver)
        => RollTier(rng, forceSilver, GoldCut, RubyCut);

    /// <summary>교체 티어 굴림 — 첫 배분보다 후하다(실버 50 / 골드 35 / 루비 15).</summary>
    public static CovenantTier RollRerollTier(System.Random rng, bool forceSilver)
        => RollTier(rng, forceSilver, RerollGoldCut, RerollRubyCut);

    // ── 내부 ─────────────────────────────────────────────
    private static CovenantTier RollTier(System.Random rng, bool forceSilver, double goldCut, double rubyCut)
    {
        if (forceSilver) return CovenantTier.Silver;
        double r = rng != null ? rng.NextDouble() : UnityEngine.Random.value;
        if (r < goldCut) return CovenantTier.Silver;
        if (r < rubyCut) return CovenantTier.Gold;
        return CovenantTier.Ruby;
    }

    /// <summary>
    /// 보장을 채울 수 있는 방어축 카드가 한 장도 없으면 아무 한 칸을 그런 카드로 바꾼다(티어는 굴린 그대로).
    /// 소모형 방어(정지)는 보장 자격이 없다 — 통화를 걸어 줄 서약이 없으면 발동조차 하지 않는다.
    /// </summary>
    private static void EnsureSurvival(List<CovenantDraftCard> cards, IReadOnlyList<string> draftable, System.Random rng)
    {
        if (cards == null || cards.Count == 0 || draftable == null) return;
        for (int i = 0; i < cards.Count; i++)
            if (CovenantPalette.IsGuaranteedSurvivalEffect(cards[i].id)) return;

        // 보장 후보도 드래프트와 같은 풀에서 — 전체 목록을 보면 잠긴 방어 카드(성역·결계)가 보장 자리로 새어 나온다.
        var pool = new List<string>();
        foreach (var id in draftable)
            if (CovenantPalette.IsGuaranteedSurvivalEffect(id)) pool.Add(id);
        if (pool.Count == 0) return;

        int slot = Next(rng, cards.Count);
        int pick = Next(rng, pool.Count);
        cards[slot] = new CovenantDraftCard(pool[pick], cards[slot].tier);
    }

    private static bool HasGuaranteedSurvival(IReadOnlyList<string> ids)
    {
        foreach (var id in ids)
            if (CovenantPalette.IsGuaranteedSurvivalEffect(id)) return true;
        return false;
    }

    /// <summary>효과 후보 중 제시된 원인 카드 전부와 짝이 되는 것만.</summary>
    private static List<string> PairableEffects(IReadOnlyList<string> effectIds,
                                                IReadOnlyList<CovenantDraftCard> causes,
                                                IReadOnlyList<CovenantBase> held)
    {
        var pool = new List<string>(effectIds.Count);
        foreach (var id in effectIds)
            if (PairsWithAll(id, causes, held, isCause: false)) pool.Add(id);
        return pool;
    }

    /// <summary>카드 id 하나가 반대쪽 열의 카드 전부와 짝이 되는가.</summary>
    private static bool PairsWithAll(string id, IReadOnlyList<CovenantDraftCard> others,
                                     IReadOnlyList<CovenantBase> held, bool isCause)
    {
        if (others == null) return true;
        for (int i = 0; i < others.Count; i++)
        {
            bool ok = isCause
                ? CovenantPalette.CanPair(id, others[i].id, held)
                : CovenantPalette.CanPair(others[i].id, id, held);
            if (!ok) return false;
        }
        return true;
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
