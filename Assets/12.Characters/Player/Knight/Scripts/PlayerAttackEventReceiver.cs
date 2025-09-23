// using System.Collections;
// using System.Collections.Generic;
// using UnityEngine;

// #region Runtime: Player attack event receiver
// // This component subscribes to Animator events (via Animation Events) and executes
// // the mapped effects, colliders and abilities from the currently equipped WeaponSO.
// public class PlayerAttackEventReceiver : MonoBehaviour
// {
//     public Transform attachRoot; // parent where effects/colliders are spawned (e.g. player root)
//     public WeaponSO equippedWeapon;
//     public int currentComboIndex = 0; // 0-based combo index
//     public bool isAir = false;


//     // Simple pools could be used instead of Instantiate in production


//     // Called by AnimationEvent: parameter is the eventIndex (int)
//     public void OnAttackEvent(int eventIndex)
//     {
//         if (equippedWeapon == null) return;


//         int slotIndex = currentComboIndex; // use combo as slot index
//                                            // choose correct anim set (ground/air) if needed - not strictly necessary here


//         // Spawn effects
//         var effects = equippedWeapon.effectPackage?.GetEffectsFor(slotIndex, eventIndex);
//         if (effects != null)
//         {
//             foreach (var e in effects)
//             {
//                 SpawnEffect(e);
//             }
//         }


//         // Spawn colliders
//         var colliders = equippedWeapon.colliderPackage?.GetCollidersFor(slotIndex, eventIndex);
//         if (colliders != null)
//         {
//             foreach (var c in colliders)
//             {
//                 StartCoroutine(SpawnColliderRoutine(c));
//             }
//         }


//         // Execute ability by slot/event mapping: simple convention - ability index == slotIndex
//         var ability = equippedWeapon.abilitySet?.GetAbilityByIndex(slotIndex);
//         if (ability != null)
//         {
//             ExecuteAbility(ability);
//         }
//     }


//     private void SpawnEffect(EffectPackageSO.EffectEntry entry)
//     {
//         // naive instantiate: in prod use Addressables.InstantiateAsync + pooling
//         if (string.IsNullOrEmpty(entry.effectKey)) return;


//         // Resolve attach transform by name under attachRoot
//         Transform attach = FindAttachTransform(entry.attachPoint);
//         Vector3 pos = attach != null ? attach.TransformPoint(entry.localPosition) : attachRoot.TransformPoint(entry.localPosition);
//         Quaternion rot = attach != null ? attach.rotation * Quaternion.Euler(entry.localEuler) : Quaternion.Euler(entry.localEuler);


//         // NOTE: Using Resources.Load for simplicity; replace with Addressables in your project
//         var prefab = Resources.Load<GameObject>(entry.effectKey);
//         if (prefab != null)
//         {
//             var go = Instantiate(prefab, pos, rot);
//             go.transform.localScale = entry.localScale;
//             if (entry.followAttach && attach != null)
//             {
//                 go.transform.SetParent(attach, true);
//             }

//             #endregion
//         }
//     }

//     private Transform FindAttachTransform(string name)
// {
//     if (attachRoot == null || string.IsNullOrEmpty(name)) return null;

//     // direct child fast-path
//     var direct = attachRoot.Find(name);
//     if (direct != null) return direct;

//     // recursive search
//     var children = attachRoot.GetComponentsInChildren<Transform>(true);
//     foreach (var t in children)
//     {
//         if (t.name == name) return t;
//     }
//     return null;
// }

// // Execute an ability: apply player motion (if any) and trigger gameplay logic (placeholder).
// private void ExecuteAbility(WeaponAbilitySetSO ability)
// {
//     if (ability == null) return;

//     // Example: lock movement while ability executes
//     // If you have a MovementController on player, call lock/unlock. Replace with your API.
//     var movement = GetComponent<IMovementLock>() as IMovementLock;
//     if (movement != null)
//     {
//         movement.LockMovement(true);
//     }

//     if (ability.applyPlayerMotion && ability.forwardDistance != 0f && ability.forwardDuration > 0f)
//     {
//         StartCoroutine(ApplyPlayerMotionRoutine(ability.forwardDistance, ability.forwardDuration));
//     }

//     // TODO: Apply damage / notify server for authoritative hit detection.
//     // For example: Server.RequestExecuteAbility(playerId, ability.abilityId, ...);
//     Debug.Log($"[Ability] Executed ability {ability.abilityId} damage:{ability.damage}");

//     // release movement lock if no motion or once motion finished.
//     // If using coroutine, the coroutine will unlock; otherwise unlock now:
//     if (!(ability.applyPlayerMotion && ability.forwardDuration > 0f))
//     {
//         if (movement != null)
//             movement.LockMovement(false);
//     }
// }

// // Coroutine to move player forward over duration (linear lerp)
// private IEnumerator ApplyPlayerMotionRoutine(float distance, float duration)
// {
//     float t = 0f;
//     Vector3 start = transform.position;
//     Vector3 target = start + transform.forward * distance;
//     while (t < duration)
//     {
//         t += Time.deltaTime;
//         float p = Mathf.Clamp01(t / duration);
//         transform.position = Vector3.Lerp(start, target, p);
//         yield return null;
//     }

//     // unlock movement if locked via IMovementLock interface
//     var movement = GetComponent<IMovementLock>() as IMovementLock;
//     if (movement != null)
//         movement.LockMovement(false);
// }

// // Spawn collider (hitbox) according to ColliderEntry data. Coroutine handles delays, motion and lifetime.
// private IEnumerator SpawnColliderRoutine(ColliderPackageSO.ColliderEntry entry)
// {
//     if (entry == null) yield break;

//     // Wait initial delay
//     if (entry.activeDelay > 0f)
//         yield return new WaitForSeconds(entry.activeDelay);

//     // Create container GameObject for hitbox
//     var go = new GameObject($"Hitbox_{(string.IsNullOrEmpty(entry.id) ? "unnamed" : entry.id)}");
//     // Parent to attachRoot so localPosition / localRotation are relative to player by default
//     if (attachRoot != null) go.transform.SetParent(attachRoot, false);
//     go.transform.localPosition = entry.localPosition;
//     go.transform.localRotation = Quaternion.Euler(entry.localEuler);

//     // Add collider component based on shape
//     Collider col = null;
//     switch (entry.shape)
//     {
//         case ColliderShape.Sphere:
//             var sph = go.AddComponent<SphereCollider>();
//             sph.radius = Mathf.Max(0.001f, entry.size.x);
//             sph.isTrigger = true;
//             col = sph;
//             break;
//         case ColliderShape.Box:
//             var box = go.AddComponent<BoxCollider>();
//             box.size = entry.size;
//             box.isTrigger = true;
//             col = box;
//             break;
//         case ColliderShape.Capsule:
//             var cap = go.AddComponent<CapsuleCollider>();
//             cap.radius = Mathf.Max(0.001f, entry.size.x);
//             cap.height = Mathf.Max(0.01f, entry.size.y);
//             cap.isTrigger = true;
//             col = cap;
//             break;
//     }

//     // Optional: Add kinematic Rigidbody to work well with physics callbacks
//     var rb = go.AddComponent<Rigidbody>();
//     rb.isKinematic = true;

//     // Add hitbox logic component
//     var hitReceiver = go.AddComponent<HitboxReceiver>();
//     hitReceiver.Initialize(entry.hitLayers, entry.abilityId, entry.maxHits, entry.clearOnHit);

//     // If motion profile present, move the collider across time
//     if (entry.motion != null && entry.motion.duration > 0f)
//     {
//         float t = 0f;
//         Vector3 start = go.transform.position;
//         Vector3 target = start + (transform.forward * entry.motion.forwardDistance); // forward in world space
//         while (t < entry.motion.duration)
//         {
//             t += Time.deltaTime;
//             float p = Mathf.Clamp01(t / entry.motion.duration);
//             float eval = entry.motion.curve != null ? entry.motion.curve.Evaluate(p) : p;
//             go.transform.position = Vector3.Lerp(start, target, eval);
//             yield return null;
//         }
//     }

//     // Keep active for duration
//     if (entry.activeDuration > 0f)
//         yield return new WaitForSeconds(entry.activeDuration);

//     // Clean up
//     if (go != null)
//         Destroy(go);
// }
// }