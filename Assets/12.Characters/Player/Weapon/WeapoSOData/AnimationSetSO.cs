using System;
using System.Collections.Generic;
using UnityEngine;


// ======= ScriptableObject definitions for modular weapon data =======
// This single file contains multiple ScriptableObject types you can split
// into separate files later if desired.


#region Animation Set
[CreateAssetMenu(menuName = "Game/AnimationSetSO")]
public class AnimationSetSO : ScriptableObject
{
[Serializable]
public class AnimationVariant
{
public string animKey; // Addressables key or local clip reference name
public float weight = 1f; // variant selection weight
}


[Serializable]
public class AnimationSlot
{
public string slotName; // e.g. "Attack1"
public List<AnimationVariant> variants = new List<AnimationVariant>();
public int expectedEventCount = 1; // how many anim events the slot uses
}


public List<AnimationSlot> slots = new List<AnimationSlot>();
}
#endregion