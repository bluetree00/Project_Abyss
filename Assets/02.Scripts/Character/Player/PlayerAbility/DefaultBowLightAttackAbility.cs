using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(menuName = "Weapon/Abilities/BowLightAttack")]
public class DefaultBowLightAttackAbility : LightAttackAbilitySO
{
    public string arrowPoolKey = "Basic_Arrow_01"; // ObjectPooler에서 사용할 키 추후 어떤 화살을 사용할지는 외부에서 결정후 그 변수를 사용.

    public override void LightAttack(CharacterController controller)
    {
        if (controller.weaponManagerSO.CurrentWeapon == null)
        {
            Debug.Log("No weapon equipped! Cannot perform attack.");
            return;
        }

        Vector3 firePoint = controller.transform.position + Vector3.up * 0.5f;
        Vector3 fireDir = controller.transform.forward;

        Quaternion arrowRotation = Quaternion.LookRotation(fireDir) * Quaternion.Euler(0, -90, 0);
        Vector3 adjustedFirePoint = firePoint - fireDir.normalized * 2f; // 0.5f 만큼 뒤로

        // firePoint  += controller.transform.right * 0.3f; 

        GameObject arrowObj = Managers.ObjectPooler.SpawnFromPool(arrowPoolKey, firePoint, arrowRotation);

        if (arrowObj != null && arrowObj.TryGetComponent(out BasicArrow arrow))
        {
            arrow.Fire(fireDir);
        }

        var weapon = controller.weaponManagerSO.CurrentWeapon;

        if (controller.CharacterData.attackComboStep >= weapon.maxAttackCount)
            controller.CharacterData.attackComboStep = 0;

        Debug.Log($"Light Combo Attack Step {controller.CharacterData.attackComboStep + 1} performed");

        controller.CharacterData.comboTimer = weapon.comboResetTime;

        // 부모 클래스의 메서드를 호출
        controller.GoToComboAttackState();

        controller.CharacterData.attackComboStep++;
        Debug.Log($"Combo Step: {controller.CharacterData.attackComboStep}");
    }
}
