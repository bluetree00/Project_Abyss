/// <summary>
/// 조립 서약 미리보기 데이터 — UI(중앙 완성 패널·카드·툴팁)가 백엔드와 동일한 티어 반영 수치를 표시하기 위한 헬퍼.
/// 런타임 효과 산출(AssembledCovenant)과 같은 티어 배율을 사용해 표시-동작 불일치를 방지한다.
/// </summary>
public readonly struct CovenantAssemblePreview
{
    public readonly bool valid;
    public readonly CovenantCategory category;

    public readonly string causeName, causeDesc, causeTag;
    public readonly CovenantTier causeTier;
    public readonly float coefficient;        // 티어 반영 유효 계수

    public readonly string effectName, effectDesc, effectTag;
    public readonly CovenantTier effectTier;
    public readonly EffectKind effectKind;
    public readonly float magnitude;          // 티어 반영 유효 magnitude
    public readonly float radius, duration;

    private CovenantAssemblePreview(
        CovenantPalette.CauseDef c, CovenantTier ct,
        CovenantPalette.EffectDef e, CovenantTier et)
    {
        valid       = true;
        category    = c.category;
        causeName   = c.name; causeDesc = c.desc; causeTag = c.tag; causeTier = ct;
        coefficient = c.coefficient * ct.CoefficientMultiplier();
        effectName  = e.name; effectDesc = e.desc; effectTag = e.tag; effectTier = et;
        effectKind  = e.kind;
        magnitude   = e.magnitude * et.MagnitudeMultiplier();
        radius      = e.radius; duration = e.duration;
    }

    /// <summary>고른 조합의 유효 수치 미리보기. 미해결 조합이면 valid=false.</summary>
    public static CovenantAssemblePreview Build(string causeId, CovenantTier causeTier,
                                                string effectId, CovenantTier effectTier)
    {
        if (CovenantPalette.TryGetCause(causeId, out var c) &&
            CovenantPalette.TryGetEffect(effectId, out var e))
            return new CovenantAssemblePreview(c, causeTier, e, effectTier);
        return default;
    }

    /// <summary>결과 문장: "OO 할 때 → XX" (중앙 미리보기용).</summary>
    public string ResultSentence => valid ? causeDesc + " → " + effectDesc : "";

    /// <summary>효과 크기 한 줄 표시(효과 종류별 단위). AoE/회복/골드=Mag×계수, 증폭=+% (Mag×계수).</summary>
    public string EffectAmountLabel()
    {
        if (!valid) return "";
        float amt = magnitude * coefficient;
        return effectKind switch
        {
            EffectKind.DamageBuff => $"피해 +{amt * 100f:0}% · {duration:0}초",
            EffectKind.AoeBurst   => $"광역 피해 ×{amt:0.0} · 반경 {radius:0.0}",
            EffectKind.Lifesteal  => $"회복 {amt:0}",
            EffectKind.GoldBurst  => $"골드 +{amt:0}",
            EffectKind.Curse      => $"받는 피해 +{amt * 100f:0}% · {duration:0}초",
            EffectKind.Execute    => $"체력 {magnitude * 100f:0}% 이하 즉사",
            _                     => $"{amt:0.0}",
        };
    }
}
