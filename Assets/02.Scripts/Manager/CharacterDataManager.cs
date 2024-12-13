using UnityEngine;

public class CharacterDataManager
{
    private static CharacterData ManagerCharacterData; //현재 캐릭터의 데이터
    private static WeaponData EquippedWeapon; //현재 무기의 데이터

    // 새로운 캐릭터 데이터를 메인으로 설정.
    public void SetCharacterData(CharacterData characterData)
    {
        if (characterData == null)
        {
            Debug.LogError("Failed to load character data, provided data is null.");
            return;
        }
        ManagerCharacterData = characterData;
        Debug.Log($"Loaded character data for {ManagerCharacterData}");
    }

    // 무기를 장착.
    public void EquipWeapon(WeaponData weaponData)
    {
        if (weaponData == null)
        {
            Debug.LogError("WeaponData is null! Cannot equip weapon.");
            return;
        }

        // 무기 효과를 캐릭터에 반영 
        ManagerCharacterData.EquipWeapon(weaponData);
    }

    // 무기를 해제.
    public void UnequipWeapon()
    {
        if (EquippedWeapon == null)
        {
            Debug.LogWarning("No weapon is currently equipped.");
            return;
        }

        // 무기 효과 제거
        ManagerCharacterData.UnequipWeapon();

        Debug.Log($"Unequipped weapon: {EquippedWeapon.weaponName}");
    }

    // 캐릭터의 주요 스텟 조정 로직
    /// <param name="statName">스텟 이름</param>
    /// <param name="value">변경할 값</param>
    public void AdjustStat(string statName, int value)
    {
        if (ManagerCharacterData == null)
        {
            Debug.LogError("No character data is set in CharacterDataManager!");
            return;
        }

        // 스텟 이름에 따라 값 조정
        switch (statName)
        {
            case "Health":
                ManagerCharacterData.maxHealth += value;
                break;
            case "AttackPower":
                ManagerCharacterData.attackPower += value;
                break;
            // 추가적인 스텟들을 여기에서 처리
            default:
                Debug.LogWarning($"Stat {statName} not recognized.");
                break;
        }
    }

}
