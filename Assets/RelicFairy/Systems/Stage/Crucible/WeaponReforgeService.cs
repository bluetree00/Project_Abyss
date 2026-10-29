// WeaponReforgeService.cs
using UnityEngine;

/// <summary>
/// 재련(10-18 재련 개편 · 기획/RelicFairy_재련개편_설계_20261018.md) — 「확률 강화」 대신 <b>재련 1회 = 확정 + 카드 고르기</b>.
/// <list type="bullet">
/// <item>근접: 강화 +3 확정(다음 3의 배수까지 · 상한까지) + 증강 고르기(<see cref="MeleeAugmentDraft"/> — 증강 단계가 3마다라 재련마다 열린다).</item>
/// <item>원거리: 파츠 카드 3장 중 1(<see cref="RangedPartsDraft"/>).</item>
/// <item>실패 · 하락 · 연속 · 잭팟은 없다.</item>
/// <item>비용 = 이번 방문 n번째(10 · 20 · 40 — 근접 · 원거리 합산 · 10-23) × 띠 × 반값 이벤트 × 대장장이의 인장(−10%/단계).
///   띠: 근접 = 강화 단계(+12 ~ ×3 · +18 ~ ×6) · 원거리 = 이 런 원거리 재련 수(5 ~ 6번째 ×3 · 7번째 ~ ×6) — 62 시뮬 안 R(10-18).</item>
/// <item>쉼터 「무기 강화」 = 무료 재련 1회(카드 포함 · D2) — 근접 · 원거리 둘 다 되면 쉼터가 고르게 한다(10-22 B7).</item>
/// </list>
/// MonoBehaviour 아님 — 재련소 컨트롤러 · 쉼터가 부른다. 표는 <see cref="WeaponEnhanceService.Table"/>.
/// </summary>
public static class WeaponReforgeService
{
    // ── Constants ─────────────────────────────────────────────────
    /// <summary>근접 재련 1회에 오르는 단계 — 증강 단계 간격과 같다(재련마다 증강 고르기).</summary>
    public const int MeleeStep = MeleeAugmentDraft.LevelStep;
    /// <summary>이번 방문 n번째 재련의 기본 비용(1 · 2 · 3번째부터).
    /// 10-23 사용자 「한 번 가면 3~5개씩 강화 — 보통 1~2 정도 할 수 있게」: 5 · 10 · 15 → 10 · 20 · 40(셋째부터 두 배씩 뛰어 보통은 둘에서 멈춘다).
    /// 시뮬(스크래치 eco_1023/final.py · 봉인기 P0 · 근접 · 원거리 같은 재료): 방문당 2.3회(3회 이상 41%) → 1.8회(1~2회 82%) · 숙련(도전 금급) 4회 이상 30% → 8%.</summary>
    private static readonly int[] VisitCost = { 10, 20, 40 };
    /// <summary>근접 띠 — 이 단계 이상이면 ×3 · ×6(진화 뒤 구간).</summary>
    public const int MeleeBandLevel1 = 12, MeleeBandLevel2 = 18;
    /// <summary>원거리 띠 — 이 런 원거리 재련이 이 번째 이상이면 ×3 · ×6.</summary>
    public const int RangedBandCount1 = 5, RangedBandCount2 = 7;
    public const float BandMult1 = 3f, BandMult2 = 6f;
    /// <summary>대장장이의 인장(기억의 제단) 1단계 = 재련 비용 −10%(10-18 · 옛 「재련 성공률 +3%p」).</summary>
    public const float SigilCostPerLevel = 0.10f;

    // ── Public Methods ────────────────────────────────────────────
    /// <summary>이번 방문 <paramref name="nth"/>번째(1부터) 재련의 기본 비용.</summary>
    public static int BaseCost(int nth) => VisitCost[Mathf.Clamp(nth, 1, VisitCost.Length) - 1];

    /// <summary>근접 강화 단계 → 재련 횟수(10-19 — 화면은 「+N」 대신 재련 횟수로 말한다 · 재련 1회 = +3 · 상한이 3의 배수가 아니어도 그 재련까지 센다).</summary>
    public static int CountAt(int level) => (Mathf.Max(0, level) + MeleeStep - 1) / MeleeStep;

    /// <summary>근접 띠 — 지금 강화 단계 기준.</summary>
    public static float MeleeBand(WeaponData w)
    {
        int lv = w != null ? w.enhanceLevel : 0;
        return lv >= MeleeBandLevel2 ? BandMult2 : lv >= MeleeBandLevel1 ? BandMult1 : 1f;
    }

    /// <summary>원거리 띠 — 이 런 <paramref name="nth"/>번째(1부터) 원거리 재련 기준.</summary>
    public static float RangedBand(int nth) => nth >= RangedBandCount2 ? BandMult2 : nth >= RangedBandCount1 ? BandMult1 : 1f;

    /// <summary>대장장이의 인장 할인 배수(1 − 0.1 × 단계).</summary>
    public static float SigilMult => Mathf.Clamp01(1f - SigilCostPerLevel * MemoryAltarService.Level(MemoryAltarCatalog.SigilSmith));

    /// <summary>재련 비용(재료) — 기본 × 띠 × 이벤트 배수 × 인장. 1 이상.</summary>
    public static int Cost(int nthThisVisit, float band, float eventMult)
        => Mathf.Max(1, Mathf.CeilToInt(BaseCost(nthThisVisit) * band * Mathf.Max(0f, eventMult) * SigilMult));

    /// <summary>근접 재련 뒤 단계 — 다음 3의 배수(옛 세이브의 3의 배수가 아닌 단계도 다음 배수까지) · 상한까지.</summary>
    public static int NextMeleeLevel(WeaponData w, EnhanceTableSO t)
    {
        if (w == null || t == null) return 0;
        int max = WeaponEnhanceService.MaxEnhance(w, t);
        return Mathf.Min((Mathf.Max(0, w.enhanceLevel) / MeleeStep + 1) * MeleeStep, max);
    }

    public static bool CanReforgeMelee(WeaponData w, EnhanceTableSO t)
        => w != null && t != null && !SkillTierResolver.IsRanged(w) && !WeaponEnhanceService.IsMaxed(w, t);

    /// <summary>
    /// 근접 재련 — 강화 +3 확정 · 증강 고르기 열기(<paramref name="extraCards"/> = 화로 「열기」 카드 +1).
    /// 재료는 호출측이 낸다. 상한이면 false.
    /// </summary>
    public static bool ReforgeMelee(WeaponData w, EnhanceTableSO t, GameRunSession run, System.Random rng, int extraCards = 0)
    {
        if (!CanReforgeMelee(w, t) || rng == null) return false;
        int before = w.enhanceLevel;
        w.enhanceLevel = NextMeleeLevel(w, t);
        w.RecomputeEnhancedStats();
        run?.ReportEnhanceLevel(w.enhanceLevel);   // 「석궁」 · 제단 조건 집계(도달한 최고 단계)
        Debug.Log($"[Reforge] 근접 {w.displayName} +{before} → +{w.enhanceLevel}");

        MeleeAugmentDraft.ExtraCards = Mathf.Max(0, extraCards);
        try
        {
            if (MeleeAugmentDraft.TryOfferForEnhance(w, w.enhanceLevel, w.augmentAfterEvolve, rng))
                w.augmentAfterEvolve = false;
        }
        finally { MeleeAugmentDraft.ExtraCards = 0; }
        return true;
    }

    /// <summary>원거리 재련 — 파츠 카드 3장(+<paramref name="extraCards"/>) 열기. 재료는 호출측이 낸다. 낼 파츠가 없으면 false.</summary>
    public static bool ReforgeRanged(System.Random rng, int extraCards = 0)
        => RangedPartsDraft.Offer(rng, RangedPartsDraft.CardCount + Mathf.Max(0, extraCards));

    /// <summary>쉼터 무료 재련을 할 수 있나 + 한 줄 설명(「카타나 +9 → +12 · 증강 고르기」 · 「파츠 카드 3장 중 하나」).</summary>
    public static bool CanFreeReforge(int slot, out string detail)
    {
        detail = null;
        var w = SlotWeapon(slot);
        var t = WeaponEnhanceService.Table;
        if (w == null) return false;
        if (SkillTierResolver.IsRanged(w))
        {
            if (!RangedPartsDraft.HasCandidates(RangedPartsState.Current) || RangedPartsDraft.IsOpen) return false;
            detail = $"파츠 카드 {RangedPartsDraft.CardCount}장 중 하나";
            return true;
        }
        if (!CanReforgeMelee(w, t) || MeleeAugmentDraft.Pending != null) return false;
        detail = $"{w.displayName} 재련 {CountAt(w.enhanceLevel) + 1}회째 · 증강 고르기";
        return true;
    }

    /// <summary>쉼터 「무기 강화」 = 무료 재련 1회(D2) — <paramref name="slot"/> 무기가 근접이면 +3 · 증강, 원거리면 파츠 카드.</summary>
    public static bool GrantFreeReforge(int slot)
    {
        if (!CanFreeReforge(slot, out _)) return false;
        var run = GameRunBootstrapper.Instance?.Run;
        var w = SlotWeapon(slot);
        var rng = new System.Random(unchecked(System.Environment.TickCount * 31 + slot));
        bool ok = SkillTierResolver.IsRanged(w) ? ReforgeRanged(rng) : ReforgeMelee(w, WeaponEnhanceService.Table, run, rng);
        if (!ok) return false;
        if (!SkillTierResolver.IsRanged(w))
        {
            var wm = run?.Player?.WeaponManager;
            if (wm != null && wm.GetCurrentSlotIndex() == slot) wm.RaiseEquippedWeaponRefreshed();
            RunFlowController.Active?.SaveNow("rest-free-reforge");   // 원거리는 카드를 고를 때 저장한다
        }
        return true;
    }

    // ── Private Methods ───────────────────────────────────────────
    private static WeaponData SlotWeapon(int slot)
    {
        var wm = GameRunBootstrapper.Instance?.Run?.Player?.WeaponManager;
        if (wm == null) return null;
        return slot == PlayerWeaponManager.Slot1 ? wm.Weapon1Data : wm.Weapon0Data;
    }
}
