using System.Collections;
using System.Collections.Generic;
using UnityEngine;


[CreateAssetMenu(menuName = "Weapon/Abilities/BowHeavyAttack")]
public class DefaultBowHeavyAttackAbility : HeavyAttackAbilitySO
{
    public string dummyEffectKey = "Bow_Charge_Effect_Heavy";
    public string dummyEffectKeyUpgraded = "Bow_Charge_Effect_Heavy";

    public string projectileKey = "Basic_Arrow_01";
    public string projectileKeyUpgraded = "Projectile_HeavyArrow";

    private GameObject dummyEffect;
    private bool upgraded = false;

    public override void HeavyAttackStartCharging(CharacterController controller)
    {

        controller.RotateTowardsMousePosition();
        controller.GoToHeavyAttackChargeStartState();
    }

    public override void HeavyAttackUpdateCharging(CharacterController controller, float chargeTime)
    {
        controller.GoToHeavyAttackChargeHoldingState();

        // 충전량이 일정 시간 이상되면 업그레이드
        if (chargeTime >= controller.heavyAttackChargeThreshold && !upgraded)
        {
            // 기존 Dummy 제거
            if (dummyEffect != null)
                Managers.ObjectPooler.ReturnToPool(dummyEffect);

            // 강화 Dummy 이펙트 생성
            dummyEffect = Managers.ObjectPooler.SpawnFromPool(dummyEffectKeyUpgraded, controller.handTransform.position, controller.handTransform.rotation);
            dummyEffect.transform.SetParent(controller.handTransform);
            upgraded = true;
        }
    }

    public override void HeavyAttackReleaseChargedAttack(CharacterController controller, float chargeTime)
    {
        controller.GoToHeavyAttackChargedAttackState();

        // Dummy 이펙트 제거
        if (dummyEffect != null)
        {
            Managers.ObjectPooler.ReturnToPool(dummyEffect);
            dummyEffect = null;
        }

        // 발사할 투사체 결정
        string projectileToSpawn = upgraded ? projectileKeyUpgraded : projectileKey;
        Vector3 firePoint = controller.handTransform.position;  // 손 위치 기준
        Vector3 fireDir = controller.transform.forward;

        Quaternion arrowRotation = Quaternion.LookRotation(fireDir);

        GameObject arrowObj = Managers.ObjectPooler.SpawnFromPool(projectileToSpawn, firePoint, arrowRotation);


        controller.GoToIdleState();
        
        upgraded = false;
    }

    public override void HeavyAttackCancelCharging(CharacterController controller)
    {
        // Dummy 이펙트 제거
        if (dummyEffect != null)
        {
            Managers.ObjectPooler.ReturnToPool(dummyEffect);
            dummyEffect = null;
        }

        controller.GoToIdleState();

        upgraded = false;
        controller.heavyAttackChargeTime = 0f;
        controller.isInChargingState = false;
    }
}
