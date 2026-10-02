using System.Collections.Generic;

/// <summary>서약서 제단에 제시되는 결과 카드 1장 — 결과 id · 굴린 등급 · 앞 절과의 이음.</summary>
public readonly struct SentenceCard
{
    public readonly string effectId;
    public readonly CovenantTier tier;
    public readonly ClauseLink link;
    public SentenceCard(string effectId, CovenantTier tier, ClauseLink link) { this.effectId = effectId; this.tier = tier; this.link = link; }
}

/// <summary>
/// 「한 장의 서약서」 카드 서비스(설계서 §3) — 문장 유효성 · 이어 쓰기 후보 · 고쳐 쓰기 후보.
/// 제시되는 카드는 <b>전부</b> 그대로 새길 수 있어야 한다(조합형 선택지 원칙 — 막힌 칸을 보여 주지 않는다).
/// 첫 쓰기(조건 + 첫 결과)는 <see cref="CovenantAssembleService.DraftBoard"/>를 그대로 쓴다(실버 고정 없이).
/// </summary>
public static class CovenantSentenceService
{
    // 이음 가중 — 즉시만 나오면 「쓰러지면 · 그동안 · 견디면」이 안 보인다
    private const double LinkWeightImmediate = 1.0;
    private const double LinkWeightOther     = 1.6;

    /// <summary>
    /// 문장 전체가 새길 수 있는 문장인가 — 길이 · 조건 × 첫 결과 봉인 짝 · 이음 문법 · 마침표 · 같은 결과 두 번 · 소모형 먹이.
    /// </summary>
    /// <param name="links">결과마다의 이음(0번은 무시 — 조건에서 즉시).</param>
    public static bool IsValid(string causeId, IReadOnlyList<string> effects, IReadOnlyList<ClauseLink> links,
                               ICollection<StatusCurrency> build = null)
    {
        if (string.IsNullOrEmpty(causeId) || effects == null || effects.Count == 0 || effects.Count > CovenantGrammar.MaxResults) return false;
        if (links == null || links.Count != effects.Count) return false;
        if (!CovenantPalette.TryGetCause(causeId, out _)) return false;

        bool immediatePrefix = true;   // 조건에서 즉시로만 이어진 절은 조건과 같은 빈도로 일어난다 → 봉인 짝을 그 절들에도 적용
        for (int i = 0; i < effects.Count; i++)
        {
            if (!CovenantPalette.TryGetEffect(effects[i], out _)) return false;
            if (i > 0 && links[i] != ClauseLink.Immediate) immediatePrefix = false;
            if (immediatePrefix && CovenantPalette.IsBannedPair(causeId, effects[i])) return false;
            if (i > 0 && !CovenantGrammar.CanLink(effects[i - 1], effects[i], links[i])) return false;
            if (CovenantGrammar.IsTerminal(effects[i]) && i != effects.Count - 1) return false;
            if (Count(effects, effects[i]) > CovenantGrammar.MaxSameEffect) return false;
            if (!CovenantGrammar.HasFeed(effects, i, effects[i], build)) return false;
        }
        return true;
    }

    /// <summary>
    /// 이어 쓰기 후보 — 문장 끝에 붙일 결과 카드 <paramref name="count"/>장(서로 다른 결과 · 전부 유효).
    /// 생존 카드(조건 없이 서는 방어) 한 장 보장은 조립 서약과 같다.
    /// </summary>
    public static List<SentenceCard> DraftAppend(string causeId, IReadOnlyList<string> effects, IReadOnlyList<ClauseLink> links,
                                                 int count, System.Random rng, ICollection<StatusCurrency> build, int step,
                                                 ICollection<string> exclude = null)
    {
        var options = AppendOptions(causeId, effects, links, build, step, exclude, rng);
        var picked  = PickDistinct(options, count, rng);
        EnsureSurvival(picked, options, rng);
        return picked;
    }

    /// <summary>
    /// 고쳐 쓰기 후보 — 결과 <paramref name="index"/>(0부터) 자리에 들어갈 다른 결과 카드. 이음은 그대로 두고,
    /// 앞 절과도 뒤 절과도 이어지는(문장 전체가 유효한) 것만.
    /// </summary>
    public static List<SentenceCard> DraftRewrite(string causeId, IReadOnlyList<string> effects, IReadOnlyList<ClauseLink> links,
                                                  int index, int count, System.Random rng, ICollection<StatusCurrency> build, int step,
                                                  ICollection<string> exclude = null)
    {
        var options = new List<SentenceCard>();
        if (effects == null || index < 0 || index >= effects.Count) return options;
        var trial = new List<string>(effects);
        foreach (var id in Unlocked(step))
        {
            if (id == Normalize(effects[index]) || (exclude != null && exclude.Contains(id))) continue;
            trial[index] = id;
            if (IsValid(causeId, trial, links, build))
                options.Add(new SentenceCard(id, CovenantTier.Silver, links[index]));
        }
        var picked = PickDistinct(options, count, rng);
        return picked;
    }

    /// <summary>조건절 고쳐 쓰기 후보 — 지금 문장의 결과들과 이어지는(첫 결과와 봉인 짝이 아닌) 원인.</summary>
    public static List<CovenantDraftCard> DraftRewriteCause(string causeId, IReadOnlyList<string> effects, IReadOnlyList<ClauseLink> links,
                                                            int count, System.Random rng, ICollection<StatusCurrency> build, int step)
    {
        var pool = new List<string>();
        foreach (var id in CovenantPalette.DraftableCauseIds(step))
            if (id != causeId && IsValid(id, effects, links, build)) pool.Add(id);
        Shuffle(pool, rng);
        var cards = new List<CovenantDraftCard>();
        for (int i = 0; i < pool.Count && cards.Count < count; i++)
            cards.Add(new CovenantDraftCard(pool[i], CovenantAssembleService.RollTier(rng, false)));
        return cards;
    }

    /// <summary>이어 쓸 수 있는 (결과, 이음) 전부 — 결과마다 이음 하나를 가중 추첨으로 골라 둔다.</summary>
    public static List<SentenceCard> AppendOptions(string causeId, IReadOnlyList<string> effects, IReadOnlyList<ClauseLink> links,
                                                   ICollection<StatusCurrency> build, int step, ICollection<string> exclude = null,
                                                   System.Random rng = null)
    {
        var options = new List<SentenceCard>();
        if (effects == null || effects.Count == 0 || effects.Count >= CovenantGrammar.MaxResultsNow) return options;
        if (CovenantGrammar.IsTerminal(effects[effects.Count - 1])) return options;

        var trialE = new List<string>(effects) { null };
        var trialL = new List<ClauseLink>(links) { ClauseLink.Immediate };
        var valid  = new List<ClauseLink>(4);
        foreach (var id in Unlocked(step))
        {
            if (exclude != null && exclude.Contains(id)) continue;
            valid.Clear();
            trialE[trialE.Count - 1] = id;
            foreach (ClauseLink l in System.Enum.GetValues(typeof(ClauseLink)))
            {
                trialL[trialL.Count - 1] = l;
                if (IsValid(causeId, trialE, trialL, build)) valid.Add(l);
            }
            if (valid.Count == 0) continue;
            options.Add(new SentenceCard(id, CovenantTier.Silver, PickLink(valid, rng)));
        }
        return options;
    }

    // ── 내부 ─────────────────────────────────────────────
    private static List<SentenceCard> PickDistinct(List<SentenceCard> options, int count, System.Random rng)
    {
        var bag = new List<SentenceCard>(options);
        Shuffle(bag, rng);
        if (count < bag.Count) bag.RemoveRange(count, bag.Count - count);
        for (int i = 0; i < bag.Count; i++)
            bag[i] = new SentenceCard(bag[i].effectId, CovenantAssembleService.RollTier(rng, false), bag[i].link);
        return bag;
    }

    /// <summary>제시된 카드에 조건 없이 서는 방어 카드가 없고 후보에 있으면 한 칸을 바꾼다(등급은 굴린 그대로).</summary>
    private static void EnsureSurvival(List<SentenceCard> cards, List<SentenceCard> options, System.Random rng)
    {
        if (cards.Count == 0) return;
        foreach (var c in cards) if (CovenantPalette.IsGuaranteedSurvivalEffect(c.effectId)) return;
        var pool = new List<SentenceCard>();
        foreach (var o in options) if (CovenantPalette.IsGuaranteedSurvivalEffect(o.effectId)) pool.Add(o);
        if (pool.Count == 0) return;
        int slot = Next(rng, cards.Count);
        var pick = pool[Next(rng, pool.Count)];
        cards[slot] = new SentenceCard(pick.effectId, cards[slot].tier, pick.link);
    }

    private static ClauseLink PickLink(List<ClauseLink> valid, System.Random rng)
    {
        if (valid.Count == 1) return valid[0];
        double total = 0;
        foreach (var l in valid) total += l == ClauseLink.Immediate ? LinkWeightImmediate : LinkWeightOther;
        double r = (rng != null ? rng.NextDouble() : UnityEngine.Random.value) * total;
        foreach (var l in valid)
        {
            r -= l == ClauseLink.Immediate ? LinkWeightImmediate : LinkWeightOther;
            if (r <= 0) return l;
        }
        return valid[valid.Count - 1];
    }

    /// <summary>기억의 제단에서 열린 결과 id(뽑기 제외 · 별칭 없이).</summary>
    private static IEnumerable<string> Unlocked(int step)
    {
        foreach (var id in CovenantPalette.EffectIds)
            if (CovenantPalette.UnlockStep(id) <= step) yield return id;
    }

    private static int Count(IReadOnlyList<string> ids, string id)
    {
        string n = Normalize(id);
        int c = 0;
        foreach (var x in ids) if (Normalize(x) == n) c++;
        return c;
    }

    private static string Normalize(string id) => CovenantPalette.TryGetEffect(id, out var e) ? e.id : id;

    private static void Shuffle<T>(List<T> list, System.Random rng)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = Next(rng, i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }

    private static int Next(System.Random rng, int exclusiveMax)
        => rng != null ? rng.Next(exclusiveMax) : UnityEngine.Random.Range(0, exclusiveMax);
}
