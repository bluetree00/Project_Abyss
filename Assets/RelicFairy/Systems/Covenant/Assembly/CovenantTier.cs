/// <summary>
/// 조립 서약 티어 — 카드의 파워 등급(실버/골드/루비).
/// 티어는 "순수 파워 축"이다(조건을 어렵게 만드는 게 아니라 보상을 키움).
/// 리스크는 원인(Cause) 종류 선택 자체가 담당. 루비 = 최상(레어).
/// </summary>
public enum CovenantTier
{
    Silver = 0,
    Gold   = 1,
    Ruby   = 2,
}

/// <summary>티어 → 배율/코드/표시명 변환. 밸런스 배율은 시작점(추후 튜닝).</summary>
public static class CovenantTierUtil
{
    // 원인 계수 배율(효과 크기 스케일). 리스크성 원인일수록 base 계수가 큼 + 티어로 추가 스케일.
    public static float CoefficientMultiplier(this CovenantTier t) => t switch
    {
        CovenantTier.Gold => 1.3f,
        CovenantTier.Ruby => 1.6f,
        _                 => 1.0f,
    };

    // 효과 magnitude 배율(증폭%/피해배수/회복량 등).
    public static float MagnitudeMultiplier(this CovenantTier t) => t switch
    {
        CovenantTier.Gold => 1.5f,
        CovenantTier.Ruby => 2.0f,
        _                 => 1.0f,
    };

    public static string Code(this CovenantTier t) => t switch
    {
        CovenantTier.Gold => "gold",
        CovenantTier.Ruby => "ruby",
        _                 => "silver",
    };

    public static string DisplayName(this CovenantTier t) => t switch
    {
        CovenantTier.Gold => "골드",
        CovenantTier.Ruby => "루비",
        _                 => "실버",
    };

    public static CovenantTier Parse(string code) => code switch
    {
        "gold"           => CovenantTier.Gold,
        "ruby" or "prism" => CovenantTier.Ruby,   // "prism"은 구 세이브 back-compat
        _                => CovenantTier.Silver,
    };
}
