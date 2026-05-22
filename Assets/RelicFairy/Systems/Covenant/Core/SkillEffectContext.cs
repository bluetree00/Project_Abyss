/// <summary>
/// 스킬 발동 시 서약이 효과를 수정할 수 있는 컨텍스트.
/// ICovenantMechanicModifier.ModifySkillEffect() 에서 ref로 전달.
/// </summary>
public struct SkillEffectContext
{
    /// <summary>발동된 스킬 슬롯</summary>
    public SkillType SkillType;

    /// <summary>피해 배율 수정 (1.0 = 기본, 2.0 = 2배)</summary>
    public float DamageMultiplier;

    /// <summary>추가 투사체 수</summary>
    public int BonusProjectile;

    /// <summary>서약이 추가 발동을 요청하는 플래그</summary>
    public bool RequestAdditionalCast;
}
