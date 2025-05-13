public interface IHeavyAttackAbility<T> where T : CharacterController
{
    void HeavyAttackStartCharging(CharacterController character); //차지를 시작하는 단계
    void HeavyAttackUpdateCharging(CharacterController character, float chargeTime);  //차지를 모으는 동안 사용될 기능
    void HeavyAttackReleaseChargedAttack(CharacterController character, float chargeTime);    // 차지량에 따른 공격 기능 변화 가능
    void HeavyAttackCancelCharging(CharacterController character); //공격이 취소 될떄 전용 초기화

    float MinChargeTime { get; }    //최대 차지 시간
    float MaxChargeTime { get; }    //최소 차지 시간
}
