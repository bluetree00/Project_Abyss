using UnityEngine;
using System.Collections;

public class PlayerAttackEventReceiver : MonoBehaviour
{
    public WeaponManagerSO weaponManager;
    public AbilityRunner abilityRunner; //NOTE : 러너는 통해 사용하더라도 결국 완성된 생성 방식을 틀어줘야함 생성완성본은 장비에서 처리해서 생성할수 있어야함
    // public EffectPool effectPool; //NOTE: 하지만 애니메이션, 이펙트, 콜라이더의 타이밍을 동일하게 맞춘다면 러너에서 조힙해서 한번에 이벤트로 호출하는것도 가능함.
    // AnimationEvent will call: OnAttackEvent(int eventIndex)
    // public void OnAttackEvent(int eventIndex)
    // {
    //     var weapon = weaponManager.equippedWeapon;
    //     if (weapon == null) return;

    //     int slotIndex = Mathf.Max(0, weaponManager.comboCount - 1); // 0-based
    //     // get event data and ability
    //     var eventData = weapon.GetEventData(slotIndex, eventIndex);
    //     var ability = weapon.GetAbility(slotIndex, eventIndex);
    //     // run
    //     abilityRunner.Run(ability, eventData, transform /* actor */);
    // }

    // // Optional: call when animation signals the end of combo
    // public void OnComboEnd()
    // {
    //     weaponManager.ResetCombo();
    // }
}
