/// <summary>
/// 스킬 비용 수정 컨텍스트.
/// ICovenantMechanicModifier.OverrideSkillCost() 에서 ref로 전달.
/// </summary>
public struct SkillCostContext
{
    /// <summary>비용이 계산될 스킬 슬롯</summary>
    public SkillType SkillType;

    /// <summary>최대 체력 대비 HP 소모 비율 (0 = 소모 없음)</summary>
    public float HpCostRatio;

    /// <summary>쿨타임 오버라이드 (-1 = 수정 없음)</summary>
    public float CooldownOverride;

    /// <summary>true이면 스킬 발동 자체를 막음</summary>
    public bool IsBlocked;
}
