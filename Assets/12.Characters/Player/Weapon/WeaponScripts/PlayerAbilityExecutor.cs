// using System.Collections;
// using System.Collections.Generic;
// using UnityEngine;


// public class PlayerAbilityExecutor : MonoBehaviour, IAbilityExecutor {
//     public WeaponController weaponController; // set on equip
//     public EffectRuntimeManager effectManager;
//     public ColliderRuntimeManager colliderManager;
//     public PlayerCombat playerCombat; // 실제 데미지, 서버 연동 등

//     public void SpawnEffectsForStep(AbilitySO ability, AbilityStep step, AbilityContext ctx) {
//         foreach (var id in step.effectIds) {
//             var effectSO = weaponController.GetEffectById(id);
//             if (effectSO != null) effectManager.SpawnEffect(effectSO, ctx.attachRoot);
//         }
//     }

//     public void SpawnCollidersForStep(AbilitySO ability, AbilityStep step, AbilityContext ctx) {
//         foreach (var id in step.colliderIds) {
//             var colSO = weaponController.GetColliderById(id);
//             if (colSO != null) colliderManager.SpawnCollider(colSO, ctx);
//         }
//     }

//     public void ApplyDamageForStep(AbilitySO ability, AbilityStep step, AbilityContext ctx) {
//         // 대부분은 콜라이더 히트콜백에서 처리. 여선 약식 즉시 처리 예:
//         playerCombat?.ApplyDamageImmediate(step.damage);
//     }

//     public void OnStepAction(AbilitySO ability, AbilityStep step, AbilityContext ctx) {
//         // 상태변화, 이펙트 트리거 등
//     }
// }