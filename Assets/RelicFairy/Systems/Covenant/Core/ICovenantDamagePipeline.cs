/// <summary>
/// 피해 계산 파이프라인에 개입하는 인터페이스.
/// PlayerController의 피해 처리 전/후에 CovenantHandler를 통해 호출된다.
/// </summary>
public interface ICovenantDamagePipeline
{
    /// <summary>플레이어가 가하는 피해 수정 (보스 피해 전 호출)</summary>
    void ModifyOutgoingDamage(ref float damage, CombatContext ctx);

    /// <summary>플레이어가 받는 피해 수정 (HP 차감 전 호출)</summary>
    void ModifyIncomingDamage(ref float damage, CombatContext ctx);

    /// <summary>
    /// HP가 0이 될 때 호출. true를 반환하면 사망을 1회 막는다 (Death Defiance).
    /// 서약 내부에서 쿨타임 관리 필요.
    /// </summary>
    bool TryPreventDeath();
}
