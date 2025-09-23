// using System.Collections;
// using System.Collections.Generic;
// using System.Threading.Tasks;
// using UnityEngine;

// public class WeaponPickup : MonoBehaviour
// {
//     public WeaponData weaponDataToGive;

//     private void OnTriggerEnter(Collider other)
//     {
//         if (other.CompareTag("Player"))
//         {
//             var player = other.GetComponent<PlayerController>();
//             if (player != null)
//             {
//                 // 비동기 래퍼 실행
//                 HandlePickupAsync(player);
//             }
//         }
//     }

//     // 래퍼 메서드는 async void로 정의 (Unity 이벤트 핸들러에서 호출 가능)
//     private async void HandlePickupAsync(PlayerController player)
//     {
//         bool success = await player.PickupWeaponAsync(weaponDataToGive);
//         if (success)
//             Destroy(gameObject);
//     }
// }
