using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(menuName = "Monster/Effect Profile")]
public class MonsterEffectProfileSO : ScriptableObject
{
    public string attackEffect;
    public string deathEffect;
    public string roarEffect;

    public AudioClip attackSound;
    public AudioClip deathSound;

    [Header("Effect Offsets")]
    public Vector3 attackEffectOffset = Vector3.zero;
    public Vector3 attackEffectRotation = Vector3.zero;

    public Vector3 deathEffectOffset = Vector3.zero;
    public Vector3 deathEffectRotation = Vector3.zero;

    public Vector3 roarEffectOffset = Vector3.zero;
    public Vector3 roarEffectRotation = Vector3.zero;
}
