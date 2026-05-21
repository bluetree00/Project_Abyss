/// <summary>
/// 강공격(Heavy Attack)에 대한 기능을 정의하는 인터페이스입니다.
/// </summary>
/// <typeparam name="T">CharacterBase를 상속하는 캐릭터 타입</typeparam>
public interface IHeavyAttackAbility<T> where T : PlayerController
{
    /// <summary>
    /// 강공격 차지를 시작할 때 호출.
    /// </summary>
    /// <param name="character">강공격을 수행하는 캐릭터</param>
    void HeavyAttackStartCharging(PlayerController character);

    /// <summary>
    /// 강공격을 차지하는 동안 매 프레임 호출.
    /// </summary>
    /// <param name="character">강공격을 수행하는 캐릭터</param>
    /// <param name="chargeTime">현재까지 누적된 차지 시간</param>
    void HeavyAttackUpdateCharging(PlayerController character, float chargeTime);

    /// <summary>
    /// 강공격 버튼에서 손을 떼면, 차지 시간에 따라 공격을을 실행.
    /// </summary>
    /// <param name="character">강공격을 수행하는 캐릭터</param>
    /// <param name="chargeTime">최종 누적된 차지 시간</param>
    void HeavyAttackReleaseChargedAttack(PlayerController character, float chargeTime);

    /// <summary>
    /// 강공격이 끝나거나 중간에 취소되었을 때 호출되어, 초기화 작업을 수행.
    /// </summary>
    /// <param name="character">강공격을 수행하던 캐릭터</param>
    void HeavyAttackCancelCharging(PlayerController character);
}
