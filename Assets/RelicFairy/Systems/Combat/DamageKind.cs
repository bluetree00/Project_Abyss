/// <summary>
/// 데미지 숫자의 출처 — 색으로 구분해 "무엇에 맞고 있는지"를 읽히게 한다.
///
/// 크리티컬은 여기 넣지 않는다. 크리는 <b>출처가 아니라 상태</b>라서
/// 어떤 출처든 크리가 날 수 있고, DamagePopup이 isCrit으로 따로 덮어쓴다.
/// </summary>
public enum DamageKind
{
    /// <summary>평타·스킬 등 일반 피해(주 피해 파이프라인).</summary>
    Normal = 0,
    /// <summary>룬 시너지·방어무시 등 2차 즉발 피해.</summary>
    Synergy = 1,
    /// <summary>화상·독 등 지속 피해(DoT) 틱.</summary>
    Dot = 2,
}
