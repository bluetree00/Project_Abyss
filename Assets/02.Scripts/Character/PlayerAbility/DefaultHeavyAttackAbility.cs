using System.Collections;
using System.Collections.Generic;
using Unity.VisualScripting;
using UnityEngine;

[CreateAssetMenu(menuName = "Weapon/Abilities/HeavyAttack")]
public class DefaultHeavyAttackAbility : HeavyAttackAbilitySO
{

    public override void HeavyAttackStartCharging(PlayerController controller)
    {
        Debug.Log("차지 시작");
        
        controller.GoToHeavyAttackChargeStartState();
    }

    public override void HeavyAttackUpdateCharging(PlayerController controller, float chargeTime)
    {
        controller.GoToHeavyAttackChargeHoldingState();

        if(chargeTime >= controller.CharacterData.heavyAttackChargeThreshold)
        {
            controller.isAttacking = true;
            controller.HeavyAttackAbility.HeavyAttackReleaseChargedAttack(controller, chargeTime);
        }
    }

    // 차지량에 따른 공격 변화 가능
    public override void HeavyAttackReleaseChargedAttack(PlayerController controller, float chargeTime)
    {
        controller.GoToHeavyAttackChargedAttackState();

           string effectName = "ShinySlash";

        int effectCount = Random.Range(10, 15); // 이펙트 생성 개수
        for (int i = 0; i < effectCount; i++)
        {
            Vector3 randomOffset = new Vector3(
                Random.Range(-0.5f, 0.5f),
                Random.Range(-0.2f, 0.5f),
                Random.Range(0.3f, 1.0f)
            );

            Quaternion randomRotation = Quaternion.Euler(
                Random.Range(-30f, 30f),
                Random.Range(0f, 360f),
                Random.Range(-30f, 30f)
            );

            Vector3 spawnPosition = controller.handTransform.position +controller.handTransform.TransformDirection(randomOffset);
            Quaternion spawnRotation = controller.handTransform.rotation * randomRotation;

            GameObject effect = Managers.ObjectPooler.SpawnFromPool(effectName, spawnPosition, spawnRotation);

            if (effect != null)
                effect.transform.SetPositionAndRotation(spawnPosition, spawnRotation);
        }
    }

    // 공격이 끝나게게 될때 전용 초기화.
    public override void HeavyAttackCancelCharging(PlayerController controller)
    {
          //내부 변수만 초기화
            controller.CharacterData.heavyAttackChargeTime = 0f;
            controller.isInChargingState = false;
    }
}
