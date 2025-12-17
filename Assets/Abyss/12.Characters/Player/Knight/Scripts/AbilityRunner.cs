using UnityEngine;
using System.Collections;
using System.Collections.Generic;

public class AbilityRunner : MonoBehaviour
{
    

    // public void Run(AttackAbilitySO ability, EventData eventData, Transform actor)
    // {
    //     // gameplay part
    //     if (ability != null)
    //     {
    //         if (ability.applyPlayerMotion)
    //             StartCoroutine(ApplyPlayerMotion(actor, ability.forwardDistance, ability.forwardDuration));
    //         // other gameplay logic (cooldown, animation override, etc.)
    //     }

    //     // visual effects
    //     if (eventData?.effects != null)
    //     {
    //         foreach (var e in eventData.effects)
    //         {
    //             var go = effectPool.Spawn(e.effectKey);
    //             PositionEffect(go.transform, e, actor);
    //             StartCoroutine(DespawnAfter(go, e.lifetime));
    //         }
    //     }

    //     // hitboxes (respect delay/duration/motion)
    //     if (eventData?.colliders != null)
    //     {
    //         foreach (var c in eventData.colliders)
    //         {
    //             StartCoroutine(SpawnHitboxRoutine(c, actor, ability));
    //         }
    //     }
    // }

    // IEnumerator ApplyPlayerMotion(Transform actor, float dist, float duration)
    // {
    //     Vector3 start = actor.position;
    //     Vector3 target = start + actor.forward * dist;
    //     float t=0;
    //     while (t < duration)
    //     {
    //         t += Time.deltaTime;
    //         actor.position = Vector3.Lerp(start, target, t/duration);
    //         yield return null;
    //     }
    // }

    // ... SpawnHitboxRoutine / PositionEffect / DespawnAfter etc. (implement as earlier discussed)
}
