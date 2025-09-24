using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

#region Collider Package
public enum ColliderShape { Sphere, Box, Capsule }


[CreateAssetMenu(menuName = "Game/ColliderPackageSO")]
public class ColliderPackageSO : ScriptableObject
{
[Serializable]
public class MotionProfile
{
public float forwardDistance = 0f;
public float duration = 0f;
public AnimationCurve curve = AnimationCurve.Linear(0, 0, 1, 1);
}


[Serializable]
public class ColliderEntry
{
public string id; // identifier used by effects to sync
public ColliderShape shape = ColliderShape.Sphere;
public Vector3 size = Vector3.one; // radius for sphere (x), box size, capsule: x=radius y=height
public Vector3 localPosition = Vector3.zero;
public Vector3 localEuler = Vector3.zero;
public float activeDelay = 0f;
public float activeDuration = 0.2f;
public LayerMask hitLayers = ~0;
public string abilityId; // which ability/damage profile to apply
public MotionProfile motion; // optional
public bool clearOnHit = true;
public int maxHits = 1;
}


[Serializable]
public class EventMapping
{
public int slotIndex;
public int eventIndex;
public List<ColliderEntry> colliders = new List<ColliderEntry>();
}


public List<EventMapping> mappings = new List<EventMapping>();


public List<ColliderEntry> GetCollidersFor(int slotIndex, int eventIndex)
{
for (int i = 0; i < mappings.Count; i++)
{
var m = mappings[i];
if (m.slotIndex == slotIndex && m.eventIndex == eventIndex)
return m.colliders;
}
return null;
}
}
#endregion