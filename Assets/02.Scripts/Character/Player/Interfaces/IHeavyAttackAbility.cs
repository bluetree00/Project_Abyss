public interface IHeavyAttackAbility<T> where T : CharacterController
{
    void HeavyAttackStartCharging(CharacterController character);
    void HeavyAttackUpdateCharging(CharacterController character, float chargeTime);
    void HeavyAttackReleaseChargedAttack(CharacterController character, float chargeTime);
    void HeavyAttackCancelCharging(CharacterController character);
}
