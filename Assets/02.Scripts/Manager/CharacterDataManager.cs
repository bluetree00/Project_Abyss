using UnityEngine;

public class CharacterDataManager
{
    public CharacterData    characterData;
    public WeaponData       weaponData;
    

    public void EqiupWeapon(string newWeapon)
    {
        WeaponData newWeaponData = Resources.Load<WeaponData>($"{newWeapon}");
        weaponData = newWeaponData;
        Debug.Log($" 무기 {newWeaponData.weaponName} 장착됨");
    }

    public float GetTotalDamage()
    {
        if (weaponData == null)
        {
            return characterData.attackPower;
        }

        return weaponData.weaponDamage + characterData.attackPower;
    }
}
