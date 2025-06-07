using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// ScriptableObject
public class DetectAbilitySO : MonsterAbilitySO
{
    public float range;

    public override IMonsterAbility CreateAbilityInstance()
    {
        return new DetectAbility(range);
    }
}
