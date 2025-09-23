using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(menuName = "Weapon/Abilities/SwordLightAttack")]
public class DefaultLightAttackAbility : LightAttackAbilitySO
{
    public override void LightAttack(PlayerController controller)
    {
        if (controller.weaponManagerSO.CurrentWeapon == null)
        {
            Debug.Log("No weapon equipped! Cannot perform attack.");
            return;
        }

        var weapon = controller.weaponManagerSO.CurrentWeapon;

        // if (controller.CharacterData.attackComboStep >= weapon.lightAttackAnimationSetSO.maxAttackCount)
        //     controller.CharacterData.attackComboStep = 0;

        Debug.Log($"Light Combo Attack Step {controller.CharacterData.attackComboStep + 1} performed");

        // controller.CharacterData.comboTimer = weapon.lightAttackAnimationSetSO.comboResetTime;

        // 부모 클래스의 메서드를 호출
        // controller.GoToComboAttackState();

        controller.CharacterData.attackComboStep++;
        Debug.Log($"Combo Step: {controller.CharacterData.attackComboStep}");
    }
    
    public override void SpawnEffect(PlayerController controller, Vector3 forwardOffset, Vector3? additionalRotation = null)
    {
        if (Managers.ObjectPooler == null)
        {
            Debug.LogError("ObjectPoolerManager is not initialized.");
            return;
        }

        Vector3 spawnPosition = controller.transform.position + controller.transform.TransformDirection(forwardOffset);
        Quaternion spawnRotation = controller.transform.rotation;

        if (additionalRotation.HasValue)
            spawnRotation *= Quaternion.Euler(additionalRotation.Value);

        GameObject effectObject = Managers.ObjectPooler.SpawnFromPool("ShinySlash", spawnPosition, spawnRotation);
        if (effectObject == null)
        {
            Debug.LogWarning($"{"ShinySlash"} 이펙트 생성 실패");
            return;
        }

        effectObject.transform.SetPositionAndRotation(spawnPosition, spawnRotation);
    }



}
