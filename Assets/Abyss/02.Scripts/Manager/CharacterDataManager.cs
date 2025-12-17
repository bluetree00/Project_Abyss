using UnityEngine;

public class CharacterDataManager
{
    private static CharacterData m_CharacterData; //현재 캐릭터의 데이터
    public CharacterData M_CharacterData { get { return m_CharacterData; } }
    private static MonsterData m_MonsterData; //현재 캐릭터의 데이터
    public MonsterData M_MonsterData { get { return m_MonsterData; } }
    private static WeaponData EquippedWeapon; //현재 무기의 데이터

    // 새로운 캐릭터 데이터를 메인으로 설정.
    public void SetCharacterData(CharacterData characterData)
    {
        if (characterData == null)
        {
            Debug.LogError("character data 를 로드하는데 실패했습니다, 현재 데이터가 null 입니다.");
            return;
        }
        m_CharacterData = characterData;
        Debug.Log($"캐릭터 데이터를 로드(캐릭터데이터매니저): {m_CharacterData}");
    }

    public void SetMonsterData(MonsterData monsterData)
    {
        if (monsterData == null)
        {
            Debug.LogError("character data 를 로드하는데 실패했습니다, 현재 데이터가 null 입니다.");
            return;
        }
        m_MonsterData = monsterData;
        Debug.Log($"캐릭터 데이터를 로드(캐릭터데이터매니저): {m_MonsterData}");
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
        m_CharacterData.EquipWeapon(weaponData);
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
        m_CharacterData.UnequipWeapon();

   
    }

    // 캐릭터의 주요 스텟 조정 로직
    /// <param name="statName">스텟 이름</param>
    /// <param name="value">변경할 값</param>
    public void AdjustStat(string statName, int value)
    {
        if (m_CharacterData == null)
        {
            Debug.LogError("No character data is set in CharacterDataManager!");
            return;
        }

        // 스텟 이름에 따라 값 조정
        switch (statName)
        {
            case "Health":
                m_CharacterData.maxHealth += value;
                break;
            case "AttackPower":
                m_CharacterData.attackPower += value;
                break;
            // 추가적인 스텟들을 여기에서 처리
            default:
                Debug.LogWarning($"Stat {statName} not recognized.");
                break;
        }
    }

}
