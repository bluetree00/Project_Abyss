using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(menuName = "Weapon/Data/BasicBowWeapon")]
public class BowWeaponData : WeaponData
{
    public override void UseQSkill(GameObject user)
    {
        Debug.Log($"🗡️ {weaponName} Q 스킬 사용");
        base.UseQSkill(user);
    }

    public override void UseESkill(GameObject user)
    {
        Debug.Log($"🗡️ {weaponName} E 스킬 사용");
        base.UseESkill(user);
    }
}
