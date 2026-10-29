/// <summary>
/// 대검 범위(10-26 무기 개편 · 설계 §4-1 · §4-4) — 평타 베기 이펙트 크기에 곱한다(그 안의 판정체가 같이 커진다 = 이펙트 = 판정).
/// 평타 = 기본 ×1.4 × 속성 검(R) 동안 ×1.3(3단계 ×1.45 · <see cref="GiantSwordBuff.RangeMultFor"/>) × (1 + 「거대한 칼날」%).
/// E 반경 = (1 + 「거대한 칼날」%) × 속성 검 동안 같은 배율(10-26 사용자 「e스킬도 r에 맞게」). 대검이 아니면 1.
/// </summary>
public static class GreatswordReach
{
    public const float BaseMult = 1.4f;

#if UNITY_EDITOR
    /// <summary>실측 98 비교용 — 0보다 크면 <see cref="BaseMult"/> 대신 쓴다(에디터만).</summary>
    public static float ProbeBaseOverride = -1f;
#endif

    /// <summary>지금 든 무기의 평타 범위 배율.</summary>
    public static float Basic(PlayerController p)
    {
        var w = Greatsword(p);
        if (w == null) return 1f;
        float b = BaseMult;
#if UNITY_EDITOR
        if (ProbeBaseOverride > 0f) b = ProbeBaseOverride;
#endif
        return b * (GiantSwordBuff.IsActive(p) ? GiantSwordBuff.RangeMultFor(w) : 1f) * Augment(w);
    }

    /// <summary>지금 든 무기의 E 반경 배율 — 속성 검 동안 평타와 같은 배율이 더 붙는다.</summary>
    public static float Skill(PlayerController p)
    {
        var w = Greatsword(p);
        return w == null ? 1f : Augment(w) * (GiantSwordBuff.IsActive(p) ? GiantSwordBuff.RangeMultFor(w) : 1f);
    }

    private static WeaponData Greatsword(PlayerController p)
    {
        var w = p != null && p.WeaponManager != null ? p.WeaponManager.CurrentWeaponData : null;
        return MeleeAugmentCatalog.KindOf(w) == MeleeWeaponKind.Greatsword ? w : null;
    }

    private static float Augment(WeaponData w) => 1f + MeleeAugments.ValueOn(w, MeleeAugmentCatalog.GreatReach) * 0.01f;
}
