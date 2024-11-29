using UnityEngine;

public class CharacterDataManager
{
    public CharacterData    characterData;
    public WeaponData       currentweaponData;
    private WeaponData      newWeaponData;

    // public CharacterDataManager(string p)
    // {   

    // }
    

    public void EquipWeapon(string newWeapon)
    {
        newWeaponData = Resources.Load<WeaponData>($"{newWeapon}");
        currentweaponData = newWeaponData;
        Debug.Log($" 무기 {newWeaponData.weaponName} 장착됨");
    }
    
    public float GetTotalDamage()
    {
        if (currentweaponData == null)
        {
            return characterData.attackPower;
        }

        return currentweaponData.weaponDamage + characterData.attackPower;
    }
}
