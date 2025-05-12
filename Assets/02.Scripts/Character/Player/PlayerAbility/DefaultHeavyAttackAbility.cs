using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(menuName = "Weapon/Abilities/HeavyAttack")]
public class DefaultHeavyAttackAbility : HeavyAttackAbilitySO
{
    [Header("차지 공격 설정")]
    public float minChargeTime = 1f;
    public float maxChargeTime = 3f;

    public override float MinChargeTime => minChargeTime;
    public override float MaxChargeTime => maxChargeTime;

    public override void HeavyAttackStartCharging(CharacterController controller)
    {
        Debug.Log("차지 시작");
        
       controller.GoToHeavyAttackChargeStartState();
    }

    public override void HeavyAttackUpdateCharging(CharacterController controller, float chargeTime)
    {
        controller.GoToHeavyAttackChargeHoldingState();
        
    }

    public override void HeavyAttackReleaseChargedAttack(CharacterController controller, float chargeTime)
    {
        
      controller.GoToHeavyAttackChargedAttackState();

    }

    public override void HeavyAttackCancelCharging(CharacterController controller)
    {

        controller.GoToHeavyAttackChargeCancelState();
    }
}
