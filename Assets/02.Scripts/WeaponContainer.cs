using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class WeaponContainer : ScriptableObject // => 예시 ) Kinght : WeaponContainer
{
    public WeaponData currentWeapon;
    public WeaponData[] ownWeapons = new WeaponData[2];


    // public virtual void SwordEffect(ref float damage) {}
}
