using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class WeaponContainer : ScriptableObject // => 예시 ) Knight : WeaponContainer
{
    /// <summary>
    /// 현재 장착중인 무기
    /// </summary>
    [SerializeField]
    public WeaponData currentWeapon;
    /// <summary>
    /// 보유중인 무기
    /// </summary>
    [SerializeField]
    public WeaponData[] ownWeapons = new WeaponData[2];
    /// <summary>
    /// 캐릭터 클래스
    /// </summary>
    [SerializeField]
    public Define.CharacterClass conClass;

    // public enum ConClass { Kinght, Archer, Mage, Thief, Warrior } => Define.cs에 있음

    // kinght : WeaponContainer
    // ConClass = Knight

    public void InitiateWeaponObject()
    {
        if (currentWeapon != null)
        {
            string weaponObjName = currentWeapon.weaponObjName;
            GameObject weaponObject = Managers.Resource.Q_Instantiate(weaponObjName, Quaternion.identity);
            if (weaponObject != null)
            {
                Debug.Log($"무기 {weaponObjName}가 성공적으로 생성되었습니다.");
            }
            else
            {
                Debug.LogError($"무기 {weaponObjName} 생성성에 실패했습니다.");
            }
        }
        else
        {
            Debug.LogError("현재 무기가 null 입니다다.");
        }
    }

}
