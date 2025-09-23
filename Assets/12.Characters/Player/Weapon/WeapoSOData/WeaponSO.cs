using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(menuName="Game/WeaponSO")]
public class WeaponSO : ScriptableObject {
    public string weaponKey;
    public string displayName;
    public string prefabKey;
    public string iconKey;
    public int stat_version;
    public float baseAttack;
    public float baseDefense;
    public int groundEndCount;
    public int airEndCount;
    public AnimationSetSO groundAnimSet; // addressable ref
    public AnimationSetSO airAnimSet;
    public WeaponAbilitySetSO abilitySet;
    public EffectPackageSO effectPackage;
    public ColliderPackageSO colliderPackage;
}


[System.Serializable]
public class AnimationSlot {
    public string slotName;
    public List<string> variantAnimKeys; // anim://... (Addressables keys)
    public int eventCount; // number of anim events expected
}


[System.Serializable]
public class EffectMapping
{
    public int slotIndex;
    public int eventIndex;
    public List<EffectEntry> effects;
}

[System.Serializable]
public class EffectEntry {
    public string effectKey; // vfx://...
    public string attachPoint; // "hand_r" ...
    public Vector3 localPos;
    public Vector3 localRot;
    public bool follow;
    public float lifetime;
    public string syncColliderId; // optional
}

// ColliderPackageSO, AbilitySetSO similar shape...
