using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>증강 카드 한 장 — 새 증강이면 단계 1, 이미 가진 것이면 다음 단계(II · III).</summary>
public readonly struct MeleeAugmentCard
{
    public readonly string Id;
    public readonly int Rank;
    public MeleeAugmentCard(string id, int rank) { Id = id; Rank = rank; }
    public bool IsUpgrade => Rank > 1;
    public MeleeAugmentDef Def => MeleeAugmentCatalog.Get(Id);
}

/// <summary>증강 고르기 화면 하나 — 같은 등급 두 장(사용자 10-15 「2가지 선택지를 랜덤으로」).</summary>
public sealed class MeleeAugmentOffer
{
    public WeaponData Weapon;
    public int Level;              // 이 화면을 연 강화 단계
    public AugmentTier Tier;
    public MeleeAugmentCard[] Cards = new MeleeAugmentCard[2];
}

/// <summary>
/// 근접 강화 증강 뽑기(설계 기획/RelicFairy_근접강화증강_설계_20261015.md §2).
/// <list type="bullet">
/// <item>근접 무기 강화가 <b>처음 닿는 단계</b>에서 성공할 때마다 화면 하나 — 하락 뒤 다시 닿으면 주지 않는다.</item>
/// <item>등급은 화면마다 굴린다(사용자 10-15 「등급도 랜덤」): 단계가 높을수록 금 · 프리즘이 잘 나온다 · 진화 직후 첫 강화는 금 이상 · 은 3연속이면 다음은 금 이상.</item>
/// <item>두 장 중 하나는 그 무기 전용 증강이 나올 확률이 높다(무기 정체성 우선 — 롤 아레나 세트 증강 폐지 교훈). 빌드 계열 각인이 높은 쪽 증강이 더 잘 나온다(×2 · 1.5 · 1.2).</item>
/// <item>이미 가진 증강은 「다음 단계」 카드로 나올 수 있다(화면당 최대 1장 · III이 끝).</item>
/// <item>리롤 런마다 <see cref="RerollsPerRun"/>번 · 한 장만 바꾼다. 고르는 창이 없으면(UI 연결 전) 왼쪽 카드로 바로 정한다.</item>
/// </list>
/// 고르기 창(UI 세션)은 <see cref="Offered"/>를 듣고 <see cref="Choose"/> · <see cref="Reroll"/>을 부른다.
/// </summary>
public static class MeleeAugmentDraft
{
    public const int RerollsPerRun = 2;
    private const int SilverPity = 3;                 // 은이 이만큼 이어지면 다음은 금 이상
    private const float ExclusiveChance = 0.4f;       // 한 장이 무기 전용 증강일 확률
    private const float UpgradeChance = 0.35f;        // 가진 증강의 강화 카드가 나올 확률(화면당 최대 1장)
    private static readonly float[] FamilyWeights = { 2f, 1.5f, 1.2f };   // 각인 1 · 2 · 3위 계열(유물 뽑기와 같은 값)

    public static int RerollsLeft { get; set; } = RerollsPerRun;
    public static MeleeAugmentOffer Pending { get; private set; }

    /// <summary>화면이 열렸다(고르기 창이 듣는다). 듣는 쪽이 없으면 왼쪽 카드로 바로 정한다.</summary>
    public static event Action<MeleeAugmentOffer> Offered;
    /// <summary>골랐다 — 무기 · 증강 id · 새 단계.</summary>
    public static event Action<WeaponData, string, int> Chosen;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() { Pending = null; RerollsLeft = RerollsPerRun; Offered = null; Chosen = null; }

    /// <summary>새 런 — 리롤 수를 채우고 걸린 화면을 버린다.</summary>
    public static void ResetForRun(int rerolls = RerollsPerRun) { RerollsLeft = rerolls; Pending = null; }

    /// <summary>
    /// 강화 성공 뒤 부른다. 아직 증강을 받지 않은 가장 낮은 단계의 화면을 연다(열었으면 true).
    /// 받은 단계는 <b>고를 때</b> 기록한다 — 고르기 전에 끄면 다음 강화 성공 때 그 단계 화면부터 다시 열린다(놓친 단계가 여럿이면 고를 때마다 이어서).
    /// 하락 뒤 다시 닿은 단계는 이미 받았으니 주지 않는다. <paramref name="justEvolved"/> = 진화 직후 첫 강화(금 이상 보장).
    /// </summary>
    public static bool TryOfferForEnhance(WeaponData w, int newLevel, bool justEvolved, System.Random rng)
    {
        if (w == null || rng == null || MeleeAugmentCatalog.KindOf(w) == MeleeWeaponKind.None) return false;
        int level = w.highestEnhanceReached + 1;
        if (Mathf.Max(newLevel, w.enhanceLevel) < level) return false;   // 하락 뒤 다시 닿음 — 이미 받았다
        if (Pending != null)
        {
            if (Pending.Weapon == w) return false;   // 같은 무기 화면이 열려 있다 — 고르면 이어서 연다
            AutoChoose();                            // 다른 무기 화면을 안 골랐다 — 왼쪽으로 정하고 넘어간다
        }

        var tier = RollTier(level, justEvolved, w.augmentSilverStreak, rng);
        w.augmentSilverStreak = tier == AugmentTier.Silver ? w.augmentSilverStreak + 1 : 0;
        var cards = Draw(w, tier, rng, 2, null);
        if (cards.Count == 0) { w.highestEnhanceReached = level; return false; }   // 풀이 다 찼다(모두 III) — 그 단계는 넘긴다
        var offer = new MeleeAugmentOffer { Weapon = w, Level = level, Tier = tier };
        for (int i = 0; i < offer.Cards.Length; i++) offer.Cards[i] = cards[Mathf.Min(i, cards.Count - 1)];
        Pending = offer;
        Debug.Log($"[MeleeAugment] +{level} {MeleeAugmentDef.TierName(tier)} — {Describe(offer.Cards[0])} | {Describe(offer.Cards[1])}");

        if (Offered != null) Offered.Invoke(offer);
        else AutoChoose();
        return true;
    }

    /// <summary>카드를 고른다(0 · 1). 무기에 단계를 올리고 룬 효과를 다시 짠다(증강 효과 · 각인 공명이 붙는다).</summary>
    public static void Choose(int index)
    {
        var offer = Pending;
        if (offer == null || index < 0 || index >= offer.Cards.Length) return;
        var card = offer.Cards[index];
        Pending = null;
        var w = offer.Weapon;
        int rank = w.AddAugment(card.Id);
        w.highestEnhanceReached = Mathf.Max(w.highestEnhanceReached, offer.Level);   // 받은 단계는 고를 때 기록
        GameRunBootstrapper.Instance?.Run?.EffectManager?.Rebuild();
        Debug.Log($"[MeleeAugment] 고름 — {w.displayName} +{offer.Level} {card.Def?.Name} {MeleeAugmentDef.RankName(rank)}");
        Chosen?.Invoke(w, card.Id, rank);

        // 놓친 단계가 남았으면(고르기 전에 껐다 · 한 번에 여러 단계) 이어서 연다
        if (w.enhanceLevel > w.highestEnhanceReached)
            TryOfferForEnhance(w, w.enhanceLevel, false, new System.Random(unchecked(System.Environment.TickCount + w.highestEnhanceReached)));
    }

    /// <summary>한 장을 다시 뽑는다(리롤 1 소모 · 같은 등급 · 다른 카드와 겹치지 않게). 못 바꾸면 false.</summary>
    public static bool Reroll(int index, System.Random rng)
    {
        var offer = Pending;
        if (offer == null || rng == null || RerollsLeft <= 0 || index < 0 || index >= offer.Cards.Length) return false;
        var keep = new HashSet<string> { offer.Cards[0].Id, offer.Cards[1].Id };
        var fresh = Draw(offer.Weapon, offer.Tier, rng, 1, keep);
        if (fresh.Count == 0) return false;
        offer.Cards[index] = fresh[0];
        RerollsLeft--;
        return true;
    }

    /// <summary>창 없이 정해야 할 때(창 미연결 · 다음 화면이 겹침 · 세이브) — 왼쪽 카드.</summary>
    public static void AutoChoose()
    {
        if (Pending != null) Choose(0);
    }

    // ── 등급 ──

    /// <summary>강화 단계별 등급 확률(은 · 금 · 프리즘) — 설계 §2-3.</summary>
    public static AugmentTier RollTier(int level, bool justEvolved, int silverStreak, System.Random rng)
    {
        float silver, gold;
        if (justEvolved)                { silver = 0f;    gold = 0.60f; }
        else if (level <= 8)            { silver = 0.70f; gold = 0.25f; }   // 쪼갠 단계 +1 ~ +8(옛 +1 ~ +4)
        else if (level <= 18)           { silver = 0.45f; gold = 0.40f; }   // +9 ~ +18(옛 +5 ~ +9)
        else                            { silver = 0.20f; gold = 0.50f; }   // +19 이상
        if (silverStreak >= SilverPity && silver > 0f) { gold += silver * (gold / (1f - silver)); silver = 0f; }   // 바닥 보장 — 은 몫을 금 · 프리즘 비율대로 나눈다
        double r = rng.NextDouble();
        if (r < silver) return AugmentTier.Silver;
        if (r < silver + gold) return AugmentTier.Gold;
        return AugmentTier.Prismatic;
    }

    // ── 카드 ──

    private static List<MeleeAugmentCard> Draw(WeaponData w, AugmentTier tier, System.Random rng, int count, HashSet<string> exclude)
    {
        var result = new List<MeleeAugmentCard>(count);
        var used = exclude != null ? new HashSet<string>(exclude) : new HashSet<string>();
        bool upgradeGiven = exclude != null && HasUpgradeIn(w, exclude);

        for (int slot = 0; slot < count; slot++)
        {
            // 가진 증강의 강화 카드(화면당 최대 1장)
            if (!upgradeGiven && rng.NextDouble() < UpgradeChance && TryPickUpgrade(w, tier, rng, used, out var up))
            {
                result.Add(up); used.Add(up.Id); upgradeGiven = true;
                continue;
            }
            bool exclusive = rng.NextDouble() < ExclusiveChance;
            if (TryPickNew(w, tier, rng, used, exclusive, out var card) || (exclusive && TryPickNew(w, tier, rng, used, false, out card)))
            {
                result.Add(card); used.Add(card.Id);
                continue;
            }
            // 이 등급 새 증강이 바닥났다 — 강화 카드, 그것도 없으면 한 등급 아래 새 증강으로 채운다(같은 카드 두 장 방지)
            if (TryPickUpgrade(w, tier, rng, used, out up)) { result.Add(up); used.Add(up.Id); upgradeGiven = true; continue; }
            for (var lower = tier - 1; lower >= AugmentTier.Silver; lower--)
                if (TryPickNew(w, lower, rng, used, false, out card)) { result.Add(card); used.Add(card.Id); break; }
        }
        return result;
    }

    private static bool HasUpgradeIn(WeaponData w, HashSet<string> ids)
    {
        foreach (var id in ids) if (w.AugmentRank(id) > 0) return true;
        return false;
    }

    private static bool TryPickNew(WeaponData w, AugmentTier tier, System.Random rng, HashSet<string> used, bool exclusiveOnly, out MeleeAugmentCard card)
    {
        card = default;
        var pool = new List<(MeleeAugmentDef def, float weight)>();
        foreach (var d in MeleeAugmentCatalog.All)
        {
            if (d.Tier != tier || used.Contains(d.Id) || w.AugmentRank(d.Id) > 0) continue;
            if (!MeleeAugmentCatalog.FitsWeapon(d, w)) continue;
            if (exclusiveOnly && d.Weapon == MeleeWeaponKind.None) continue;
            if (!IsMeaningful(d, w)) continue;
            pool.Add((d, FamilyWeight(d.Family)));
        }
        if (!Pick(pool, rng, out var def)) return false;
        card = new MeleeAugmentCard(def.Id, 1);
        return true;
    }

    private static bool TryPickUpgrade(WeaponData w, AugmentTier tier, System.Random rng, HashSet<string> used, out MeleeAugmentCard card)
    {
        card = default;
        var pool = new List<(MeleeAugmentDef def, float weight)>();
        foreach (var (id, rank) in w.AugmentList())
        {
            var d = MeleeAugmentCatalog.Get(id);
            if (d == null || rank >= MeleeAugmentDef.MaxRank || used.Contains(id) || d.Tier > tier) continue;   // 낮은 등급 증강도 높은 화면에 강화 카드로
            if (!MeleeAugmentCatalog.FitsWeapon(d, w)) continue;
            pool.Add((d, FamilyWeight(d.Family)));
        }
        if (!Pick(pool, rng, out var def)) return false;
        card = new MeleeAugmentCard(def.Id, w.AugmentRank(def.Id) + 1);
        return true;
    }

    /// <summary>지금 이 무기에 제시해도 의미가 있나 — 막힌 짝은 제시 단계에서 거른다(정본 규칙).</summary>
    private static bool IsMeaningful(MeleeAugmentDef d, WeaponData w)
    {
        switch (d.Id)
        {
            case "aug_mark_spread":   // 유물 표식 — 유물이 없으면 표식도 없다
                return AppBootstrapper.Instance != null && AppBootstrapper.Instance.Loadout != null && AppBootstrapper.Instance.Loadout.Relic != null;
            case "aug_imprint_echo":  // 각인이 하나도 없으면 공명할 계열이 없다
                foreach (var f in BuildFamilyRules.All) if (BuildImprint.Count(f) > 0) return true;
                return false;
            case MeleeAugmentCatalog.HeavyHand:   // 대검 평타는 이미 강타(계수 1.6 ≥ 문턱 1.5)
                return MeleeAugmentCatalog.KindOf(w) != MeleeWeaponKind.Greatsword;
            case "aug_thunder":       // 강타가 나는 무기만 — 대검이거나 묵직한 손을 가졌다
                return MeleeAugmentCatalog.KindOf(w) == MeleeWeaponKind.Greatsword || w.AugmentRank(MeleeAugmentCatalog.HeavyHand) > 0;
            case MeleeAugmentCatalog.TwinWave:    // 검기를 가져야 갈래가 생긴다
                return w.AugmentRank(MeleeAugmentCatalog.SwordWave) > 0;
            case MeleeAugmentCatalog.KatanaDraw:     return w.skillE != null && w.skillE.behavior is HolySlashBehaviorSO;
            case MeleeAugmentCatalog.KatanaPhantom:  return w.skillQ != null && w.skillQ.behavior is PhantomDanceBehaviorSO;
            case MeleeAugmentCatalog.GreatPerimeter: return w.skillE != null && w.skillE.behavior is PerimeterGuardBehaviorSO;
            case MeleeAugmentCatalog.GreatFinal:     return w.skillQ != null && w.skillQ.behavior is FinalStrikeBehaviorSO;
            case "aug_nameless_afterimage":          return w.skillQ != null && w.skillQ.behavior is IasenSlashBehaviorSO;
            case "aug_katana_gale":   return w.HasForm && w.form.FormId == "windblade";   // 칼바람 형이 있어야 쌓인다
            case MeleeAugmentCatalog.GreatSplit:     return w.HasForm && w.form.FormId == "weight";
            case MeleeAugmentCatalog.NamelessSheath: return w.CanEvolve;   // 진화할 수 있어야 칼집이 바뀐다
            default:
                return true;
        }
    }

    private static float FamilyWeight(BuildFamily f)
    {
        if (f == BuildFamily.None) return 1f;
        int better = 0, mine = BuildImprint.Count(f);
        if (mine <= 0) return 1f;
        foreach (var o in BuildFamilyRules.All) if (o != f && BuildImprint.Count(o) > mine) better++;
        return better < FamilyWeights.Length ? FamilyWeights[better] : 1f;
    }

    private static bool Pick(List<(MeleeAugmentDef def, float weight)> pool, System.Random rng, out MeleeAugmentDef picked)
    {
        picked = null;
        float sum = 0f;
        foreach (var (_, wgt) in pool) sum += wgt;
        if (sum <= 0f) return false;
        double r = rng.NextDouble() * sum;
        foreach (var (def, wgt) in pool)
        {
            r -= wgt;
            if (r <= 0) { picked = def; return true; }
        }
        picked = pool[pool.Count - 1].def;
        return true;
    }

    private static string Describe(MeleeAugmentCard c)
    {
        var d = c.Def;
        return d == null ? c.Id : $"{d.Name} {MeleeAugmentDef.RankName(c.Rank)}({d.Text(c.Rank)})";
    }
}
