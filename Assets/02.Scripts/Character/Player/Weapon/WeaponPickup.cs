using System.Collections;
using System.Collections.Generic;
using UnityEngine;
public class WeaponPickup : MonoBehaviour
{
    public WeaponData weaponDataToGive; // 이 오브젝트에 붙어있는 무기 데이터

   private void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("Player"))
        {
            var player = other.GetComponent<CharacterController>();
            if (player != null)
            {
                // 습득 성공 여부를 받아서 처리
                bool success = player.PickupWeapon(weaponDataToGive);

                if (success)
                    Destroy(gameObject); // 습득 성공 시에만 무기 제거
            }
        }
    }

}
