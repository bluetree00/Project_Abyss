/// <summary>
/// 조립 서약 효과 크기의 스케일 방식 — 원인 계수(Coef)를 효과 크기(Mag)에 어떻게 태울지.
///
/// 계수와 티어가 곱으로만 쌓이면 "루비 효과 × 고위험 원인"이 화면을 즉시 지워버린다
/// (예: 학살@루비 계수 6.4 × 초신성@루비 배율 2.0 = 원래의 12.8배). 효과의 성격마다
/// 위험한 방향이 달라서 — 피해는 커져도 되지만 무적 시간은 안 된다 — 감쇠를 종류로 나눈다.
///
/// 이름에 Covenant 접두사가 붙은 이유: 전역 네임스페이스의 <c>ScaleMode</c>는
/// <see cref="UnityEngine.ScaleMode"/>(GUI 스케일)를 <b>가려버린다</b>. 그러면 그 이름을 쓰던
/// 서드파티 데모 스크립트가 통째로 컴파일에 실패한다(KriptoFX·Hovl Studio). 짧은 이름의 대가가 너무 크다.
/// </summary>
public enum CovenantScaleMode
{
    /// <summary>L: Mag × Coef. 계수를 그대로 태운다.</summary>
    Linear,
    /// <summary>D: Mag × (1 + (Coef−1) × 0.35). 완만 증가 — 대부분의 공격/증폭 효과.</summary>
    Damped,
    /// <summary>S: Mag × √Coef. 강한 감쇠 — 절대량이 큰 값(보호막 등).</summary>
    Sqrt,
    /// <summary>C: clamp(round(Mag × √Coef), 1, cap). 횟수/충전 같은 정수량.</summary>
    Count,
    /// <summary>N: Mag. 계수 무시 — 스케일하면 위험한 값(무적 시간 등).</summary>
    None,
}

/// <summary>
/// 효과가 담당하는 축. 드래프트 방어축 보장(효과 3장 중 최소 1장 Survival)에 쓴다 —
/// 공격 효과만 뽑혀 "맞으면 죽는" 빌드로 몰리는 것을 막는다.
/// </summary>
public enum EffectAxis
{
    Offense,
    Survival,
    Utility,
}

/// <summary>
/// 효과가 다루는 상태 통화 4종(화상/취약/보호막/기세).
/// A급 범위에서는 <b>카드 배지 표시 전용</b>이다 — 통화끼리 주고받는 환전소는 C급이라 이번엔 없다.
/// </summary>
public enum StatusCurrency
{
    None,
    Burn,        // 화상
    Vulnerable,  // 취약
    Shield,      // 보호막
    Momentum,    // 기세
}

/// <summary>축/통화 표시명 — 카드 배지·미리보기 공용.</summary>
public static class EffectTaxonomy
{
    public static string DisplayName(this EffectAxis axis) => axis switch
    {
        EffectAxis.Survival => "생존",
        EffectAxis.Utility  => "보조",
        _                   => "공격",
    };

    public static string DisplayName(this StatusCurrency c) => c switch
    {
        StatusCurrency.Burn       => "화상",
        StatusCurrency.Vulnerable => "취약",
        StatusCurrency.Shield     => "보호막",
        StatusCurrency.Momentum   => "기세",
        _                         => null,
    };

    /// <summary>카드 배지 문구. 통화가 없으면 축만. 예: "생존 · 보호막".</summary>
    public static string Badge(EffectAxis axis, StatusCurrency status)
    {
        string s = status.DisplayName();
        return s == null ? axis.DisplayName() : axis.DisplayName() + " · " + s;
    }
}
