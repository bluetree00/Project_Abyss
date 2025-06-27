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
}
