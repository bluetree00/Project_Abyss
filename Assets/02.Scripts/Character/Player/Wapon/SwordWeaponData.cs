using UnityEngine;

[CreateAssetMenu(menuName = "Weapon/Data/SwordWeapon")]
public class SwordWeaponData : WeaponData
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
