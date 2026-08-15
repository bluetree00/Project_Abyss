using UnityEngine;

/// <summary>
/// 조립 서약 효과 크기 계산의 <b>단일 소스</b>.
///
/// 예전에는 같은 식(Mag × Coef)이 런타임(AssembledCovenant)과 미리보기(CovenantAssemblePreview)에
/// 따로 적혀 있었다. 한쪽만 고치면 카드에 쓰인 숫자와 실제로 터지는 숫자가 갈라진다 —
/// 그건 밸런스 버그가 아니라 신뢰 문제라 눈에 띄지도 않고 재현도 안 된다.
/// 그래서 표시와 동작이 <b>같은 함수</b>를 부르도록 여기로 모았다.
/// </summary>
public static class CovenantMath
{
    /// <summary>Damped 모드의 감쇠 계수. 계수 초과분(Coef−1)을 이 비율만큼만 반영한다.</summary>
    public const float DampedFactor = 0.35f;

    /// <summary>광역 효과 반경 상한(m). 화면을 통째로 덮는 발동을 막는 안전장치.</summary>
    public const float AoeRadiusCap = 5.5f;

    /// <summary>「박차」 공속 보너스 / 이속 보너스 비율(이속 +4% 기준 공속 +3%).</summary>
    public const float MomentumAtkSpeedRatio = 0.75f;

    /// <summary>「박차」 최대 중첩.</summary>
    public const int MomentumMaxStacks = 5;

    // ── 상태 통화 소모 비율(스펙 고정값) ──────────────────
    // 이 셋은 티어·계수로 스케일하지 않는다. "얼마를 먹는가"까지 커지면 소모형이 걸어주는 서약을
    // 통째로 굶겨 그물이 아니라 독식이 된다. 커지는 쪽은 '먹은 것을 무엇으로 바꾸는가'(Effective)다.
    /// <summary>「기폭」이 먹는 화상·출혈 잔량 비율.</summary>
    public const float DetonateFraction = 0.60f;
    /// <summary>「흡정」이 먹는 화상·출혈 잔량 비율.</summary>
    public const float SanguineFraction = 0.40f;
    /// <summary>「수확」이 먹는 화상·출혈 잔량 비율.</summary>
    public const float HarvestFraction  = 0.30f;
    /// <summary>「수확」 1회 골드.</summary>
    public const int   HarvestGold      = 3;

    /// <summary>「출혈」 최대 중첩.</summary>
    public const int BleedMaxStacks = 5;

    // ── 원인 형상별 질적 변형 ────────────────────────────
    /// <summary>「박차」 기동 원인 변형의 중첩 상한(이속 위주 → 더 오래 쌓인다).</summary>
    public const int MomentumMobilityMaxStacks = 8;
    /// <summary>「박차」 변형에서 <b>주축이 아닌</b> 스탯이 받는 비율.</summary>
    public const float MomentumOffAxisRatio = 0.35f;
    /// <summary>「박차」 처치 원인 변형의 지속 배수.</summary>
    public const float MomentumKillDurationMult = 2f;

    /// <summary>「격노」 위험 원인 변형의 증폭 배수(대신 인접이 흩어지면 즉시 꺼진다).</summary>
    public const float FuryDangerBonus = 1.25f;

    /// <summary>「처형」 임계에 곱해지는 상태 통화 1종당 배수.</summary>
    public const float ExecuteStatusMult = 1.5f;
    /// <summary>「처형」 경계 원인 변형(최저 HP 표식)의 임계 배수.</summary>
    public const float ExecuteBoundaryMult = 2f;

    /// <summary>
    /// 「처형」 임계 절대 상한. 통화 3종(화상·출혈·취약)이 다 걸리면 배수만 3.375배라
    /// 상한이 없으면 임계가 1을 넘어 <b>체력과 무관하게</b> 즉사한다.
    /// </summary>
    public const float ExecuteThresholdCap = 0.60f;

    /// <summary>원인 형상에 따른 「박차」 중첩 상한.</summary>
    public static int MomentumStackCap(CauseClass cls)
        => cls == CauseClass.Mobility ? MomentumMobilityMaxStacks : MomentumMaxStacks;

    // ── 원재료 ──────────────────────────────────────────
    /// <summary>티어 반영 원인 계수(스케일 적용 전 원값). UI의 "봉인 계수" 표기가 이 값이다.</summary>
    public static float RawCoef(in CovenantPalette.CauseDef cause, CovenantTier causeTier)
        => Mathf.Max(0f, cause.coefficient * causeTier.CoefficientMultiplier());

    /// <summary>티어 반영 효과 크기(계수 적용 전).</summary>
    public static float RawMag(in CovenantPalette.EffectDef effect, CovenantTier effectTier)
        => effect.magnitude * effectTier.MagnitudeMultiplier();

    // ── 유효 수치 ───────────────────────────────────────
    /// <summary>
    /// 최종 효과 크기. 효과의 <see cref="CovenantScaleMode"/>로 계수를 태운 뒤 cap(>0일 때)으로 자른다.
    /// Count 모드는 <see cref="EffectiveCount"/>와 같은 값을 float으로 돌려준다.
    /// </summary>
    public static float Effective(in CovenantPalette.EffectDef effect, CovenantTier effectTier,
                                  in CovenantPalette.CauseDef cause, CovenantTier causeTier)
    {
        float mag  = RawMag(effect, effectTier);
        float coef = RawCoef(cause, causeTier);

        if (effect.mode == CovenantScaleMode.Count)
            return EffectiveCount(effect, effectTier, cause, causeTier);

        float v = effect.mode switch
        {
            CovenantScaleMode.Linear => mag * coef,
            CovenantScaleMode.Damped => mag * (1f + (coef - 1f) * DampedFactor),
            CovenantScaleMode.Sqrt   => mag * Mathf.Sqrt(coef),
            _                        => mag,   // None
        };

        if (effect.cap > 0f) v = Mathf.Min(v, effect.cap);
        return Mathf.Max(0f, v);
    }

    /// <summary>정수량(충전 횟수 등) 유효값. cap이 0이면 상한 없음.</summary>
    public static int EffectiveCount(in CovenantPalette.EffectDef effect, CovenantTier effectTier,
                                     in CovenantPalette.CauseDef cause, CovenantTier causeTier)
    {
        float mag  = RawMag(effect, effectTier);
        float coef = RawCoef(cause, causeTier);
        int   max  = effect.cap > 0f ? Mathf.RoundToInt(effect.cap) : int.MaxValue;
        return Mathf.Clamp(Mathf.RoundToInt(mag * Mathf.Sqrt(coef)), 1, max);
    }

    /// <summary>상한이 걸린 광역 반경.</summary>
    public static float EffectiveRadius(in CovenantPalette.EffectDef effect)
        => Mathf.Min(effect.radius, AoeRadiusCap);
}
