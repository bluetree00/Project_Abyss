using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(menuName = "Weapon/Abilities/SwordLightAttack")]
public class DefaultLightAttackAbility : LightAttackAbilitySO 
{
    public override void LightAttack(CharacterController controller)
    {
        if (controller.weaponManagerSO.CurrentWeapon == null)
        {
            Debug.Log("No weapon equipped! Cannot perform attack.");
            return;
        }

        var weapon = controller.weaponManagerSO.CurrentWeapon;

        if (controller.CharacterData.attackComboStep >= weapon.lightAttackAnimationSetSO.maxAttackCount)
            controller.CharacterData.attackComboStep = 0;

        Debug.Log($"Light Combo Attack Step {controller.CharacterData.attackComboStep + 1} performed");

        controller.CharacterData.comboTimer = weapon.lightAttackAnimationSetSO.comboResetTime;

        // 부모 클래스의 메서드를 호출
        controller.GoToComboAttackState();

        controller.CharacterData.attackComboStep++;
        Debug.Log($"Combo Step: {controller.CharacterData.attackComboStep}");
    }
}