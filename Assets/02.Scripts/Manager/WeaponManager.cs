using UnityEngine;
using UnityEngine.PlayerLoop;

public class WeaponManager
{
    private GameObject WeaponContainerObj;
    private GameObject WM_Obj;
    private CharacterData characterData;
    private WeaponData weaponData;
    private OwnWeapon[] ownWeapons;

    public class OwnWeapon{
        int weaponIndex;
    }  

    public void WeaponInit(Object @object)
    {
        WM_Obj = @object as GameObject;
        characterData = null;
        weaponData = null;
    }

}
