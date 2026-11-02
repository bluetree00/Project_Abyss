using UnityEngine;

/// <summary>
/// 형 단련 규칙(순수) — 재련소 컨트롤러와 실측 도구가 함께 쓴다. 단계마다 변주 하나(D2). 기획 장비개선 §4-5.
/// 10-30 사용자 「무기형 고르기는 잘 보기 힘드니 특정 재련 단계에서 지급」 — 재련소에서 강화재료로 변주를 골라 벼리던 것을 걷고,
/// 진화 뒤 재련 <see cref="TierIIReforge"/>회째에 형 Ⅱ · <see cref="TierIIIReforge"/>회째에 형 Ⅲ을 저절로 준다(<see cref="GrantForReforge"/> · 변주 = 형 SO의 재련 변주).
/// 재련 2 · 4회째는 카타나 그림자 분신이 늘어 재련마다 체감이 하나씩 온다.
/// </summary>
public static class WeaponFormService
{
    public const int MaxTier = 3;
    /// <summary>형 Ⅱ · Ⅲ을 주는 진화 뒤 재련 횟수(1회째 = +12 · 3회째 = +18).</summary>
    public const int TierIIReforge = 1, TierIIIReforge = 3;

    /// <summary>형을 받았다(무기 · 새 단계 · 변주) — 재련소 화면 · 알림이 쓴다.</summary>
    public static event System.Action<WeaponData, int, WeaponFormSO.VariantDef> Granted;

    /// <summary>진화 뒤 재련 <paramref name="reforges"/>회에 맞는 형 단계(1 ~ 3).</summary>
    public static int TierForReforges(int reforges)
        => 1 + (reforges >= TierIIReforge ? 1 : 0) + (reforges >= TierIIIReforge ? 1 : 0);

    /// <summary>그 형 단계를 주는 진화 뒤 재련 횟수(2 → 1회째 · 3 → 3회째 · 그 밖 0).</summary>
    public static int ReforgeForTier(int tier) => tier == 2 ? TierIIReforge : tier == 3 ? TierIIIReforge : 0;

    /// <summary>재련 뒤 부른다 — 재련 횟수에 닿은 형 단계를 그 단계 재련 변주와 함께 준다(모자란 단계를 차례로 · 준 단계 수).</summary>
    public static int GrantForReforge(WeaponData w)
    {
        if (w == null || !w.HasForm) return 0;
        int want = Mathf.Min(MaxTier, TierForReforges(MeleeAugmentCatalog.ReforgesSinceEvolution(w)));
        int given = 0;
        while (w.formTier < want)
        {
            int next = w.formTier + 1;
            w.formTier = next;
            if (!w.form.TryGetReforgeVariant(next, out var v)) continue;
            w.AddFormVariant(v.id);
            given++;
            Debug.Log($"[Form] {w.displayName} 형 {w.form.DisplayName} {next}단계 — {v.displayName}(재련 단계 지급)");
            Granted?.Invoke(w, next, v);
        }
        return given;
    }

    /// <summary>다음에 받을 형 — 단계 · 그 단계를 주는 재련 횟수 · 변주(재련소 화면 「재련 n회째 — 형 Ⅱ 쌍풍」). 형이 없거나 Ⅲ까지 받았으면 false.</summary>
    public static bool NextGrant(WeaponData w, out int tier, out int atReforge, out WeaponFormSO.VariantDef v)
    {
        tier = 0; atReforge = 0; v = default;
        if (w == null || !w.HasForm || w.formTier >= MaxTier) return false;
        tier = w.formTier + 1;
        atReforge = ReforgeForTier(tier);
        return w.form.TryGetReforgeVariant(tier, out v);
    }

    /// <summary>
    /// 다음 단련 단계와 비용 — 10-30부터 늘 false(형은 재련 단계에서 저절로 받는다 · <see cref="GrantForReforge"/>).
    /// 재련소 화면의 「형 단련 — 변주를 고른다」 칸이 이것으로 열렸다.
    /// </summary>
    public static bool CanTemper(WeaponData w, out int nextTier, out int cost)
    {
        nextTier = 0; cost = 0;
        return false;
    }

    /// <summary>변주를 골라 벼린다. 다음 단계의 변주가 아니거나 강화재료가 모자라면 false(아무것도 안 바뀐다).</summary>
    public static bool TryTemper(WeaponData w, RunFuelBank fuel, string variantId)
    {
        if (!CanTemper(w, out int nextTier, out int cost)) return false;
        if (!w.form.TryGetVariant(variantId, out var v) || v.tier != nextTier) return false;
        if (fuel == null || !fuel.TrySpend(FuelKind.EnhanceMaterial, cost)) return false;
        w.formTier = nextTier;
        w.AddFormVariant(variantId);
        return true;
    }
}
