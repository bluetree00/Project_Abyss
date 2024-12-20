using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class WeaponContainer : ScriptableObject
{
    public WeaponData currentWeapon;
    public WeaponData[] ownWeapons = new WeaponData[2];

    public void SetCurrentWeapon()
    {
        currentWeapon = Managers.Weapon.GetData<WeaponData>();
        ownWeapons[0] = currentWeapon;
    }
}
