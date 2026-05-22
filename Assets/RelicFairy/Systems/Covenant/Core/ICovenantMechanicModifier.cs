/// <summary>
/// 캐릭터 메커닉 자체를 변경하는 인터페이스.
/// 스킬 동작 변형, 스킬 비용 수정 등 깊은 개입이 필요한 서약에서 사용.
/// </summary>
public interface ICovenantMechanicModifier
{
    /// <summary>플레이어 바인딩 직후 호출. 메커닉 수정 초기화에 사용.</summary>
    void OnBoundToPlayer(PlayerController player);

    /// <summary>
    /// 스킬 발동 직전 효과 수정. ref ctx의 필드를 변경해 동작을 바꾼다.
    /// </summary>
    void ModifySkillEffect(SkillType skill, ref SkillEffectContext ctx);

    /// <summary>
    /// 스킬 비용 수정. true를 반환하면 수정된 ctx가 적용된다.
    /// </summary>
    bool OverrideSkillCost(SkillType skill, ref SkillCostContext ctx);
}
