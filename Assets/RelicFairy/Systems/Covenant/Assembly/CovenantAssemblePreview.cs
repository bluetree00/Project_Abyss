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
    public readonly StatusRole     role;      // 통화를 거는가(Apply) 먹는가(Consume) — 시너지 판정축

    /// <summary>원인 형상 — 질적 변형(기폭 단일/광역, 처형 표식 등)이 미리보기 문구에도 반영돼야 한다.</summary>
    public readonly CauseClass causeClass;
    public readonly bool       causeTargeted;

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
        role        = e.role;
        causeClass  = c.cls;
        causeTargeted = c.targeted;
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
            EffectKind.StatBuff   => MomentumLabel(),
            EffectKind.BleedStack => $"출혈 공격력×{effective * 100f:0}% · {duration:0}초 (최대 {CovenantMath.BleedMaxStacks}중첩)",
            EffectKind.Detonate   => causeTargeted
                ? $"화상·출혈 {CovenantMath.DetonateFraction * 100f:0}% 기폭 · 파편 ×{effective:0.0} · 반경 {radius:0.0}"
                : $"주변 전체 화상·출혈 {CovenantMath.DetonateFraction * 100f:0}% 분산 기폭 · 반경 {radius:0.0}",
            EffectKind.Harvest    => $"화상·출혈 {CovenantMath.HarvestFraction * 100f:0}% 수확 → 쿨감 {effective:0.0}초 · 골드 +{CovenantMath.HarvestGold}",
            EffectKind.Arcflash   => causeClass == CauseClass.Skill
                ? $"감전 1중첩 · 순차 체인 {effectiveCount + CovenantMath.ArcflashChainBonus + 1}체 (간격 {CovenantMath.ArcflashChainHop:0}m)"
                : $"감전 1중첩 · 방사형 {effectiveCount + 1}체 · 반경 {radius:0.0}",
            EffectKind.Stasis     => $"감전 전량 소모 → 반경 {radius:0.0} 기절 (1중첩당 {effective:0.00}초 · 최대 {CovenantMath.StasisStunCap:0.0}초)",
            EffectKind.Ward       => $"받는 피해 -{effective * 100f:0}% · {duration:0}초 (상태 걸린 적 1체당 -{CovenantMath.WardPerSteepedEnemy * 100f:0}%p · 최대 -{CovenantMath.WardReductionCap * 100f:0}%)",
            _                     => $"{effective:0.0}",
        };
    }

    /// <summary>「박차」는 원인 형상에 따라 주축이 바뀐다(기동=이속·상한↑ / 처치=공속·지속2배).</summary>
    private string MomentumLabel()
    {
        float off = effective * CovenantMath.MomentumOffAxisRatio;
        int   cap = CovenantMath.MomentumStackCap(causeClass);
        return causeClass switch
        {
            CauseClass.Mobility => $"이속 +{effective * 100f:0}% · 공속 +{off * 100f:0}% (최대 {cap}중첩)",
            CauseClass.Kill     => $"공속 +{effective * 100f:0}% · 이속 +{off * 100f:0}% · 지속 {duration * CovenantMath.MomentumKillDurationMult:0}초 (최대 {cap}중첩)",
            _                   => $"이속 +{effective * 100f:0}% · 공속 +{effective * CovenantMath.MomentumAtkSpeedRatio * 100f:0}% (최대 {cap}중첩)",
        };
    }

    /// <summary>카드 통화 배지 문구. 예: "생존 · 보호막".</summary>
    public string Badge => valid ? EffectTaxonomy.Badge(axis, status) : "";

    // ── 시너지 힌트 ──────────────────────────────────────
    /// <summary>
    /// 이미 가진 서약과 <b>통화가 물리는지</b> 한 줄로 알려준다. 그물의 요점은 서약 하나의 세기가 아니라
    /// 걸기→터뜨리기의 순서인데, 조립 화면은 그걸 보여줄 방법이 하나도 없었다 —
    /// 플레이어가 그물을 짜려면 지금 고르는 카드가 무엇과 물리는지 <b>고르는 순간</b> 보여야 한다.
    /// 반환 null = 물리는 것 없음(UI는 줄을 감춘다).
    /// </summary>
    public string SynergyHint(System.Collections.Generic.IReadOnlyList<CovenantBase> held)
    {
        if (!valid || held == null || role == StatusRole.None) return null;

        for (int i = 0; i < held.Count; i++)
        {
            if (held[i] is not AssembledCovenant a || !a.Resolved) continue;
            if (a.Role == StatusRole.None || a.Role == role) continue;              // 같은 방향끼리는 안 물린다
            if (!EffectTaxonomy.SameFamily(status, a.Status)) continue;

            string coin = (role == StatusRole.Consume ? a.Status : status).DisplayName();
            return role == StatusRole.Consume
                ? $"시너지  「{a.EffectName}」이(가) 걸어 둔 {coin}을(를) 이 서약이 먹는다"
                : $"시너지  이 서약이 거는 {coin}을(를) 「{a.EffectName}」이(가) 먹는다";
        }
        return null;
    }

    /// <summary>효과 카드 한 장이 보유 서약과 물리는지(카드 테두리 글로우용). 원인과 무관하게 통화·역할만 본다.</summary>
    public static bool HasSynergy(string effectId, System.Collections.Generic.IReadOnlyList<CovenantBase> held)
    {
        if (held == null || !CovenantPalette.TryGetEffect(effectId, out var e) || e.role == StatusRole.None)
            return false;

        for (int i = 0; i < held.Count; i++)
            if (held[i] is AssembledCovenant a && a.Resolved &&
                a.Role != StatusRole.None && a.Role != e.role &&
                EffectTaxonomy.SameFamily(e.status, a.Status))
                return true;
        return false;
    }
}
