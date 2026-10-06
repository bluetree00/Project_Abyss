/// <summary>
/// 형 단련 규칙(순수) — 재련소 컨트롤러와 실측 도구가 함께 쓴다. 확정 성공 · 하락 없음 · 단계마다 변주 하나(D2).
/// 기획 장비개선 §4-5.
/// </summary>
public static class WeaponFormService
{
    public const int MaxTier = 3;

    /// <summary>다음 단련 단계와 비용. 형이 없거나 Ⅲ까지 벼렸으면 false.</summary>
    public static bool CanTemper(WeaponData w, out int nextTier, out int cost)
    {
        nextTier = 0; cost = 0;
        if (w == null || !w.HasForm || w.formTier >= MaxTier) return false;
        nextTier = w.formTier + 1;
        cost = w.form.TemperCost(nextTier);
        return true;
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
