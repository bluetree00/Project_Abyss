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

    /// <summary>이 화면에 전설 카드가 있나(창 · 소리 — e2). 카드마다 등급은 <c>card.Def.Tier</c>(전설 카드만 화면 등급과 다르다).</summary>
    public bool HasLegend
    {
        get
        {
            foreach (var c in Cards) if (c.Def != null && c.Def.IsLegend) return true;
            return false;
        }
    }
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
    /// <summary>
    /// 증강 화면이 열리는 강화 단계 간격 — +3 · +6 · +9 …(사용자 10-16 「특정 강화 단계에서만 증강을 고르는 구조」).
    /// 진화 전 상한 +12까지 4번 · 진화 뒤 +24까지 8번 · 전설 +30까지 10번 — 한 장 한 장이 무게를 갖게(롤 아레나도 정해진 라운드에만 고른다).
    /// </summary>
    public const int LevelStep = 3;
    private const int SilverPity = 3;                 // 은이 이만큼 이어지면 다음은 금 이상
    private const float ExclusiveChance = 0.4f;       // 한 장이 무기 전용 증강일 확률
    private const float UpgradeChance = 0.35f;        // 가진 증강의 강화 카드가 나올 확률(화면당 최대 1장)
    private static readonly float[] FamilyWeights = { 2f, 1.5f, 1.2f };   // 각인 1 · 2 · 3위 계열(유물 뽑기와 같은 값)

    // 전설(10-24 W7 · 설계 §3 권장) — 화면마다 따로 한 번 · 기본 2% + 제단 「증강의 계시」 Lv당 +2%p(최대 8%) · 진화 직후 화면 ×2 · 바닥 보장 없음
    public const float  LegendBase = 0.02f, LegendPerAltarLevel = 0.02f, LegendEvolvedMult = 2f;
    /// <summary>기억의 제단 노드 id(e2가 MemoryAltarCatalog에 더한다 — 없으면 단계 0).</summary>
    public const string LegendAltarNode = "augment_revelation";

    public static int RerollsLeft { get; set; } = RerollsPerRun;
    /// <summary>이번 재련소 방문에서 화면마다 더 내는 카드 수(화로 「열기」 · 10-18 재련 개편) — 재련이 화면을 열 때만 잠깐 켠다.</summary>
    public static int ExtraCards { get; set; }
    /// <summary>이번 방문에만 쓰는 다시 뽑기(화로 「풍요」). 런 몫보다 먼저 쓴다. 방을 나가면 0.</summary>
    public static int VisitRerolls { get; set; }
    /// <summary>쓸 수 있는 다시 뽑기(런 몫 + 이번 방문 몫) — 고르기 창이 보인다.</summary>
    public static int RerollsAvailable => RerollsLeft + VisitRerolls;
    public static MeleeAugmentOffer Pending { get; private set; }

    /// <summary>화면이 열렸다(고르기 창이 듣는다). 듣는 쪽이 없으면 왼쪽 카드로 바로 정한다.</summary>
    public static event Action<MeleeAugmentOffer> Offered;
    /// <summary>골랐다 — 무기 · 증강 id · 새 단계.</summary>
    public static event Action<WeaponData, string, int> Chosen;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() { Pending = null; RerollsLeft = RerollsPerRun; ExtraCards = 0; VisitRerolls = 0; Offered = null; Chosen = null; }

    /// <summary>이 강화 단계에서 증강을 고르나 — 재련소 · 무기 정보 창이 「다음 증강 +N」을 보일 때 쓴다.</summary>
    public static bool IsAugmentLevel(int level) => level > 0 && level % LevelStep == 0;

    /// <summary>받은 마지막 증강 단계 다음의 증강 단계(+3 · +6 · +9 …).</summary>
    public static int NextAugmentLevel(int highestReached) => (Mathf.Max(0, highestReached) / LevelStep + 1) * LevelStep;

    /// <summary>새 런 — 리롤 수를 채우고 걸린 화면을 버린다.</summary>
    public static void ResetForRun(int rerolls = RerollsPerRun) { RerollsLeft = rerolls; Pending = null; LegendAugment.ResetForRun(); }

    /// <summary>이 화면에서 전설이 나올 확률(제단 노드 설명 「화면마다 n% · 런마다 약 m%」도 이것을 읽는다).</summary>
    public static float LegendChance(bool justEvolved)
        => (LegendBase + LegendPerAltarLevel * MemoryAltarService.Level(LegendAltarNode)) * (justEvolved ? LegendEvolvedMult : 1f);

    /// <summary>화면 수 <paramref name="screens"/>번 중 전설을 한 번 이상 볼 확률(노드 설명용 · 진화 직후 제외).</summary>
    public static float LegendChancePerRun(int screens = 7) => 1f - Mathf.Pow(1f - LegendChance(false), Mathf.Max(1, screens));

    /// <summary>
    /// 강화 성공 뒤 부른다. 아직 받지 않은 가장 낮은 <b>증강 단계</b>(<see cref="LevelStep"/>의 배수)에 닿았으면 그 화면을 연다(열었으면 true).
    /// 받은 단계는 <b>고를 때</b> 기록한다 — 고르기 전에 끄면 다음 강화 성공 때 그 단계 화면부터 다시 열린다(놓친 단계가 여럿이면 고를 때마다 이어서).
    /// 하락 뒤 다시 닿은 단계는 이미 받았으니 주지 않는다. <paramref name="justEvolved"/> = 진화 직후 첫 강화(금 이상 보장).
    /// </summary>
    public static bool TryOfferForEnhance(WeaponData w, int newLevel, bool justEvolved, System.Random rng)
    {
        if (w == null || rng == null || MeleeAugmentCatalog.KindOf(w) == MeleeWeaponKind.None) return false;
        int level = NextAugmentLevel(w.highestEnhanceReached);
        if (Mathf.Max(newLevel, w.enhanceLevel) < level) return false;   // 아직 다음 증강 단계 전 · 하락 뒤 다시 닿음(이미 받았다)
        if (Pending != null)
        {
            if (Pending.Weapon == w) return false;   // 같은 무기 화면이 열려 있다 — 고르면 이어서 연다
            AutoChoose();                            // 다른 무기 화면을 안 골랐다 — 왼쪽으로 정하고 넘어간다
        }

        var tier = RollTier(level, justEvolved, w.augmentSilverStreak, rng);
        w.augmentSilverStreak = tier == AugmentTier.Silver ? w.augmentSilverStreak + 1 : 0;
        int count = 2 + Mathf.Max(0, ExtraCards);   // 화로 「열기」 — 이번 방문 카드 +1장
        var cards = Draw(w, tier, rng, count, null);
        if (cards.Count == 0) { w.highestEnhanceReached = level; return false; }   // 풀이 다 찼다(모두 III) — 그 단계는 넘긴다
        // 전설 — 화면마다 따로 한 번 굴려 걸리면 카드 한 장을 전설로(10-24 W7)
        if (rng.NextDouble() < LegendChance(justEvolved) && TryPickLegend(w, rng, cards, out var legend))
            cards[rng.Next(cards.Count)] = legend;
        var offer = new MeleeAugmentOffer { Weapon = w, Level = level, Tier = tier, Cards = new MeleeAugmentCard[count] };
        for (int i = 0; i < offer.Cards.Length; i++) offer.Cards[i] = cards[Mathf.Min(i, cards.Count - 1)];
        Pending = offer;
        Debug.Log($"[MeleeAugment] +{level} {MeleeAugmentDef.TierName(tier)} — {Describe(offer.Cards[0])} | {Describe(offer.Cards[1])}");

        if (Offered != null) Offered.Invoke(offer);
        else AutoChoose();
        return true;
    }

    /// <summary>
    /// 시작 선물(10-29 설계 §6-1 · 해방기 · 악몽 런 시작) — 강화 단계와 상관없이 증강 화면을 하나 연다(같은 고르기 창 · 재련 기록에 남는다 ·
    /// 받은 강화 단계는 건드리지 않는다). 근접 무기가 아니면(원거리) false — 선물 칸을 숨긴다(4c).
    /// </summary>
    public static bool OfferGift(WeaponData w, AugmentTier tier = AugmentTier.Gold, int count = 2, System.Random rng = null)
    {
        if (!CanOfferGift(w)) return false;
        rng ??= new System.Random(unchecked(System.Environment.TickCount));
        if (Pending != null)
        {
            if (Pending.Weapon == w) return false;
            AutoChoose();
        }
        var cards = Draw(w, tier, rng, Mathf.Max(1, count), null);
        if (cards.Count == 0) return false;
        var offer = new MeleeAugmentOffer { Weapon = w, Level = 0, Tier = tier, Cards = cards.ToArray() };
        Pending = offer;
        Debug.Log($"[MeleeAugment] 시작 선물 {MeleeAugmentDef.TierName(tier)} — {string.Join(" | ", System.Array.ConvertAll(offer.Cards, Describe))}");
        if (Offered != null) Offered.Invoke(offer);
        else AutoChoose();
        return true;
    }

    /// <summary>시작 선물 증강 칸을 보일 수 있나 — 근접 무기(무형검 · 카타나 · 대검)만. 원거리면 칸을 숨긴다(4c).</summary>
    public static bool CanOfferGift(WeaponData w) => w != null && MeleeAugmentCatalog.KindOf(w) != MeleeWeaponKind.None;

    /// <summary>카드를 고른다(0 · 1). 무기에 단계를 올리고 룬 효과를 다시 짠다(증강 효과 · 각인 공명이 붙는다).</summary>
    public static void Choose(int index)
    {
        var offer = Pending;
        if (offer == null || index < 0 || index >= offer.Cards.Length) return;
        var card = offer.Cards[index];
        Pending = null;
        var w = offer.Weapon;
        int rank = w.AddAugment(card.Id);
        w.AppendReforgeLog(card.Id, rank);   // 10-19 재련 기록 — 재련마다 고른 것(재련소 목록)
        w.highestEnhanceReached = Mathf.Max(w.highestEnhanceReached, offer.Level);   // 받은 단계는 고를 때 기록
        GameRunBootstrapper.Instance?.Run?.EffectManager?.Rebuild();
        Debug.Log($"[MeleeAugment] 고름 — {w.displayName} +{offer.Level} {card.Def?.Name} {MeleeAugmentDef.RankName(rank)}");
        Chosen?.Invoke(w, card.Id, rank);

        // 놓친 증강 단계가 남았으면(고르기 전에 껐다 · 한 번에 여러 단계) 이어서 연다
        if (w.enhanceLevel >= NextAugmentLevel(w.highestEnhanceReached))
            TryOfferForEnhance(w, w.enhanceLevel, false, new System.Random(unchecked(System.Environment.TickCount + w.highestEnhanceReached)));
    }

    /// <summary>한 장을 다시 뽑는다(리롤 1 소모 · 같은 등급 · 다른 카드와 겹치지 않게). 못 바꾸면 false.</summary>
    public static bool Reroll(int index, System.Random rng)
    {
        var offer = Pending;
        if (offer == null || rng == null || RerollsAvailable <= 0 || index < 0 || index >= offer.Cards.Length) return false;
        var keep = new HashSet<string>();
        foreach (var c in offer.Cards) keep.Add(c.Id);
        var fresh = Draw(offer.Weapon, offer.Tier, rng, 1, keep);
        if (fresh.Count == 0) return false;
        offer.Cards[index] = fresh[0];
        if (VisitRerolls > 0) VisitRerolls--;   // 이번 방문 몫(화로 「풍요」)부터
        else RerollsLeft--;
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

    /// <summary>이 무기에 맞고 아직 없는 전설 한 장(빌드 계열 가중 · 제시 거름 그대로). 다 가졌으면 false.</summary>
    private static bool TryPickLegend(WeaponData w, System.Random rng, List<MeleeAugmentCard> current, out MeleeAugmentCard card)
    {
        card = default;
        var pool = new List<(MeleeAugmentDef def, float weight)>();
        foreach (var d in MeleeAugmentCatalog.All)
        {
            if (!d.IsLegend || w.AugmentRank(d.Id) > 0 || !MeleeAugmentCatalog.FitsWeapon(d, w) || !IsMeaningful(d, w)) continue;
            bool dup = false;
            foreach (var c in current) if (c.Id == d.Id) { dup = true; break; }
            if (!dup) pool.Add((d, FamilyWeight(d.Family)));
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
            if (d == null || d.IsLegend || d.IsGrowth || rank >= MeleeAugmentDef.MaxRank || used.Contains(id) || d.Tier > tier) continue;   // 낮은 등급 증강도 높은 화면에 강화 카드로 · 전설 · 재련 자람(10-26)은 단계 없음
            if (!MeleeAugmentCatalog.FitsWeapon(d, w)) continue;
            pool.Add((d, FamilyWeight(d.Family)));
        }
        if (!Pick(pool, rng, out var def)) return false;
        card = new MeleeAugmentCard(def.Id, w.AugmentRank(def.Id) + 1);
        return true;
    }

    /// <summary>지금 이 무기에 제시해도 의미가 있나 — 막힌 짝은 제시 단계에서 거른다(정본 규칙). 칼집 진화 변환 · 옛 세이브 바꿈도 쓴다.</summary>
    internal static bool IsMeaningful(MeleeAugmentDef d, WeaponData w)
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
            case MeleeAugmentCatalog.KatanaDraw:     return w.skillE != null && w.skillE.behavior is HolySlashBehaviorSO;
            case MeleeAugmentCatalog.LegendIssen:    return w.skillE != null && w.skillE.behavior is HolySlashBehaviorSO;   // E가 돌진(성광베기)이어야 길이 있다
            case MeleeAugmentCatalog.KatanaPhantom:  return w.skillQ != null && w.skillQ.behavior is PhantomDanceBehaviorSO;
            case MeleeAugmentCatalog.GreatPerimeter: return w.skillE != null && w.skillE.behavior is PerimeterGuardBehaviorSO;
            case MeleeAugmentCatalog.GreatFinal:     return w.skillQ != null && w.skillQ.behavior is ElementSwordBehaviorSO;   // 「속성 검 연장」 — R이 속성 검이어야(10-26 R 개편 뒤 옛 도약 내려찍기 검사가 남아 한 번도 안 나왔다)
            case "aug_katana_gale":   return w.HasForm && w.form.FormId == "windblade";   // 칼바람 형이 있어야 쌓인다
            case MeleeAugmentCatalog.GreatSplit:     return w.HasForm && w.form.FormId == "weight";
            case MeleeAugmentCatalog.NamelessSheath: return w.CanEvolve;   // 진화할 수 있어야 칼집이 바뀐다
            case "aug_element_edge":  // 룬 판에 속성이 하나도 없으면 터질 속성이 없다
                return ElementEdgeAugment.TopElement(out _);
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
        return d == null ? c.Id : d.IsLegend ? $"[전설] {d.Name}({d.Text(1)})" : $"{d.Name} {MeleeAugmentDef.RankName(c.Rank)}({d.Text(c.Rank)})";
    }
}
