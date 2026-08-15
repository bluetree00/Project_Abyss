/// <summary>
/// 조립 서약 미리보기 데이터 — UI(중앙 완성 패널·카드·툴팁)가 백엔드와 동일한 수치를 표시하기 위한 헬퍼.
/// 계산은 전부 <see cref="CovenantMath"/>에 위임한다 — 런타임(AssembledCovenant)과 <b>같은 함수</b>를 불러야
/// 카드에 적힌 숫자와 실제로 터지는 숫자가 갈라지지 않는다.
/// </summary>
public readonly struct CovenantAssemblePreview
{
    public readonly bool valid;
    public readonly CovenantCategory category;

    public readonly string causeName, causeDesc, causeTag;
    public readonly CovenantTier causeTier;
    public readonly float coefficient;        // 티어 반영 유효 계수(스케일 전 원값 — "봉인 계수" 표기)

    public readonly string effectName, effectDesc, effectTag;
    public readonly CovenantTier effectTier;
    public readonly EffectKind effectKind;
    public readonly float magnitude;          // 티어 반영 magnitude(계수 전)
    public readonly float effective;          // 스케일·상한까지 반영된 최종 수치 = 실제 동작값
    public readonly int   effectiveCount;     // 정수량(충전 횟수 등)
    public readonly float radius, duration;

    public readonly EffectAxis     axis;
    public readonly StatusCurrency status;

    private CovenantAssemblePreview(
        CovenantPalette.CauseDef c, CovenantTier ct,
        CovenantPalette.EffectDef e, CovenantTier et)
    {
        valid       = true;
        category    = c.category;
        causeName   = c.name; causeDesc = c.desc; causeTag = c.tag; causeTier = ct;
        coefficient = CovenantMath.RawCoef(c, ct);
        effectName  = e.name; effectDesc = e.desc; effectTag = e.tag; effectTier = et;
        effectKind  = e.kind;
        magnitude   = CovenantMath.RawMag(e, et);
        effective   = CovenantMath.Effective(e, et, c, ct);
        effectiveCount = CovenantMath.EffectiveCount(e, et, c, ct);
        radius      = CovenantMath.EffectiveRadius(e);
        duration    = e.duration;
        axis        = e.axis;
        status      = e.status;
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

    /// <summary>효과 크기 한 줄 표시(효과 종류별 단위). 값은 전부 실제 동작값(effective)이다.</summary>
    public string EffectAmountLabel()
    {
        if (!valid) return "";
        return effectKind switch
        {
            EffectKind.DamageBuff => $"피해 +{effective * 100f:0}% · {duration:0}초",
            EffectKind.AoeBurst   => $"광역 피해 ×{effective:0.0} · 반경 {radius:0.0}",
            EffectKind.Shield     => $"보호막 {effective:0}",
            EffectKind.GoldBurst  => $"골드 +{effective:0}",
            EffectKind.Curse      => $"받는 피해 +{effective * 100f:0}% · {duration:0}초",
            EffectKind.Execute    => $"체력 {effective * 100f:0}% 이하 즉사",
            EffectKind.Burn       => $"화상 공격력×{effective * 100f:0}% · {duration:0}초",
            EffectKind.Invincible => $"무적 {effective:0.0}초",
            EffectKind.DeathSave  => $"치명 피해 생존 {effectiveCount}회",
            EffectKind.StatBuff   => $"이속 +{effective * 100f:0}% · 공속 +{effective * CovenantMath.MomentumAtkSpeedRatio * 100f:0}% (최대 {CovenantMath.MomentumMaxStacks}중첩)",
            _                     => $"{effective:0.0}",
        };
    }

    /// <summary>카드 통화 배지 문구. 예: "생존 · 보호막".</summary>
    public string Badge => valid ? EffectTaxonomy.Badge(axis, status) : "";
}
