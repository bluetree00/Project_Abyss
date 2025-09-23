using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

#region Effect Package
[CreateAssetMenu(menuName = "Game/EffectPackageSO")]
public class EffectPackageSO : ScriptableObject
{
[Serializable]
public class EffectEntry
{
public string id; // optional id to match colliders or logic
public string effectKey; // Addressables key for VFX prefab
public string attachPoint = "weapon_tip"; // e.g. hand_r, weapon_tip, root
public Vector3 localPosition = Vector3.zero;
public Vector3 localEuler = Vector3.zero;
public Vector3 localScale = Vector3.one;
public bool followAttach = false;
public float lifetime = 2f;
public string syncColliderId; // optional
}


[Serializable]
public class EventMapping
{
public int slotIndex; // which attack slot (combo index)
public int eventIndex; // which anim event within that slot
public List<EffectEntry> effects = new List<EffectEntry>();
}


public List<EventMapping> mappings = new List<EventMapping>();


public List<EffectEntry> GetEffectsFor(int slotIndex, int eventIndex)
{
for (int i = 0; i < mappings.Count; i++)
{
var m = mappings[i];
if (m.slotIndex == slotIndex && m.eventIndex == eventIndex)
return m.effects;
}
return null;
}
}
#endregion