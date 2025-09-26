using System;
using UnityEngine;

[CreateAssetMenu(menuName = "Game/EffectSO")]
public class WeaponEffectSO : ScriptableObject
{
    [Header("Identifier")]
    public string id; // ex: "sword_slash_01" (optional but recommended, unique)

    [Header("Addressable")]
    public string addressableKey; // Addressables key or bundle URL

    [Header("Attach / Transform")]
    public string attachPoint = "weapon_tip"; // e.g. hand_r, weapon_tip, root
    public Vector3 localPosition = Vector3.zero;
    public Vector3 localEuler = Vector3.zero;
    public Vector3 localScale = Vector3.one;
    public bool followAttach = false;

    [Header("Lifetime / Behavior")]
    public float lifetime = 2f;
    public string syncColliderId; // optional: 콜라이더 id와 동기화

    [Header("Variant / Meta")]
    public string variantTag; // optional: "large", "ice", etc.
    public int recommendedPoolSize = 4;
}
