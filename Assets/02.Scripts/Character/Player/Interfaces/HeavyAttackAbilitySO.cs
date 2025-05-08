using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public abstract class HeavyAttackAbilitySO : ScriptableObject, IHeavyAttackAbility<CharacterController>
{
    public abstract void HeavyAttackStartCharging(CharacterController character); // 모으기 시작 애니메이션 등
    public abstract void HeavyAttackUpdateCharging(CharacterController character, float chargeTime); // 모으기 시 적용될 업데이트트
    public abstract void HeavyAttackReleaseChargedAttack(CharacterController character, float chargeTime); //모으기가 끝나고 공격 전환
    public abstract void HeavyAttackCancelCharging(CharacterController character); // 강곡격 취소시 애니메이션 등
}
