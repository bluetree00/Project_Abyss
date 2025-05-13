using System.Collections;
using System.Collections.Generic;
using Unity.VisualScripting;
using UnityEngine;

[CreateAssetMenu(menuName = "Weapon/Abilities/HeavyAttack")]
public class DefaultHeavyAttackAbility : HeavyAttackAbilitySO
{

    public override void HeavyAttackStartCharging(CharacterController controller)
    {
        Debug.Log("차지 시작");
        
        controller.GoToHeavyAttackChargeStartState();
    }

    public override void HeavyAttackUpdateCharging(CharacterController controller, float chargeTime)
    {
        controller.GoToHeavyAttackChargeHoldingState();

        if(chargeTime >= controller.heavyAttackChargeThreshold)
        {
            controller.isAttacking = true;
            controller.HeavyAttackAbility.HeavyAttackReleaseChargedAttack(controller, chargeTime);
        }
    }

    // 차지량에 따른 공격 변화 가능
    public override void HeavyAttackReleaseChargedAttack(CharacterController controller, float chargeTime)
    {
        controller.GoToHeavyAttackChargedAttackState();

    }

    // 공격이 끝나게게 될때 전용 초기화.
    public override void HeavyAttackCancelCharging(CharacterController controller)
    {
          //내부 변수만 초기화
            controller.heavyAttackChargeTime = 0f;
            controller.isInChargingState = false;
    }
}
