using System;
using System.Collections.Generic;
using UnityEngine;

public enum ColliderShape { Sphere, Box, Capsule }

[CreateAssetMenu(menuName = "Game/ColliderSO")]
public class WeaponColliderSO : ScriptableObject
{
    [Header("Identifier")]
    public string id; // unique id, ex: "sword_hit_01"

    [Header("Shape")]
    public ColliderShape shape = ColliderShape.Sphere;

    [Tooltip("radius for sphere (x), box size, capsule: x=radius y=height")]
    public Vector3 size = Vector3.one;

    [Header("Transform")]
    public Vector3 localPosition = Vector3.zero;
    public Vector3 localEuler = Vector3.zero;

    [Header("Timing")]
    public float activeDelay = 0f;
    public float activeDuration = 0.2f;

    [Header("Hit / Logic")]
    public LayerMask hitLayers = ~0;
    public string abilityId; // what ability/damage profile to apply
    public bool clearOnHit = true;
    public int maxHits = 1;

    [Header("Motion (optional)")]
    public MotionProfile motion;

    [Header("Runtime prefab (Addressables key or local prefab)")]
    public string prefabAddressableKey; // if you spawn a prefab for hitbox/visuals

    [Serializable]
    public class MotionProfile {
        public float forwardDistance = 0f;
        public float duration = 0f;
        public AnimationCurve curve = AnimationCurve.Linear(0,0,1,1);
    }
}
