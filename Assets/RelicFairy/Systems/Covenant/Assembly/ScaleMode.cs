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
/// 효과가 다루는 상태 통화 6종(화상/출혈/감전/취약/보호막/기세).
/// 배지 표시에 더해, <see cref="StatusRole"/>과 짝지어 "서약끼리 물리는가"(시너지 힌트)를 판정한다.
/// 실제 상태는 <see cref="CovenantStatus"/>(환전소)가 기존 채널에 위임해 다룬다 — 저장소는 늘지 않는다.
/// </summary>
public enum StatusCurrency
{
    None,
    Burn,        // 화상
    Bleed,       // 출혈
    Vulnerable,  // 취약
    Shield,      // 보호막
    Momentum,    // 기세
    Shock,       // 감전 — 슬로우 채널("shock") 재사용. 쌓아 두었다가 「정지」로 터뜨린다.
}

/// <summary>
/// 효과가 통화를 <b>거는가 먹는가</b>. 그물이 성립하려면 방향이 필요하다 —
/// 부여만 있으면 상태가 쌓이기만 하고, 소모만 있으면 먹을 게 없다.
/// 소모형은 어떤 상태도 부여하지 않는다(부여+소모를 겸하면 스스로를 먹여 무한 기폭이 된다).
/// </summary>
public enum StatusRole
{
    None,
    Apply,     // 부여 — 통화를 건다
    Consume,   // 소모 — 걸린 통화를 먹고 다른 것으로 바꾼다
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
        StatusCurrency.Bleed      => "출혈",
        StatusCurrency.Vulnerable => "취약",
        StatusCurrency.Shield     => "보호막",
        StatusCurrency.Momentum   => "기세",
        StatusCurrency.Shock      => "감전",
        _                         => null,
    };

    /// <summary>지속피해 계열(화상·출혈)인지.</summary>
    public static bool IsDot(this StatusCurrency c)
        => c == StatusCurrency.Burn || c == StatusCurrency.Bleed;

    /// <summary>
    /// 두 통화가 같은 군인지 — 소모형(기폭·수확)은 화상과 출혈을 가리지 않고 먹기 때문에
    /// 둘을 한 군으로 본다. 이게 없으면 「출혈」×「기폭」 같은 실제로 물리는 조합이 힌트에 안 뜬다.
    /// 감전은 제 군을 따로 갖는다(「정지」만 먹는다).
    /// </summary>
    public static bool SameFamily(StatusCurrency a, StatusCurrency b)
        => a != StatusCurrency.None && (a == b || (a.IsDot() && b.IsDot()));

    /// <summary>카드 배지 문구. 통화가 없으면 축만. 예: "생존 · 보호막".</summary>
    public static string Badge(EffectAxis axis, StatusCurrency status)
    {
        string s = status.DisplayName();
        return s == null ? axis.DisplayName() : axis.DisplayName() + " · " + s;
    }
}
