using System.Collections;
using System.Collections.Generic;
using UnityEngine;


[CreateAssetMenu(menuName = "Weapon/Abilities/BowHeavyAttack")]
public class DefaultBowHeavyAttackAbility : HeavyAttackAbilitySO
{


    public override void HeavyAttackStartCharging(CharacterController character)
    {
        Debug.Log("차지 시작");


    }

    public override void HeavyAttackUpdateCharging(CharacterController character, float chargeTime)
    {

    }

    public override void HeavyAttackReleaseChargedAttack(CharacterController character, float chargeTime)
    {


        // 공격 애니메이션
       // character.Animator?.SetTrigger("HeavyAttack");

        // 데미지 계산 등은 상태에서 할 수도 있고 여기서 직접 할 수도 있음
    }

    public override void HeavyAttackCancelCharging(CharacterController character)
    {
       
    }
}

