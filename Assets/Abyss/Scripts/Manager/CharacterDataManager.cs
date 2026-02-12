using System;
using UnityEngine;

public sealed class CharacterDataManager
{
    private static CharacterData m_CharacterData;
    public CharacterData M_CharacterData => m_CharacterData;

    private static MonsterData m_MonsterData;
    public MonsterData M_MonsterData => m_MonsterData;

    private static WeaponData m_EquippedWeapon;
    public WeaponData EquippedWeaponData => m_EquippedWeapon;

    /// <summary>
    /// 캐릭터/무기/스탯 등 HUD/UI가 갱신되어야 하는 변화가 발생했을 때 호출
    /// </summary>
    public event Action OnChanged;

    public void SetCharacterData(CharacterData characterData)
    {
        if (characterData == null)
        {
            Debug.LogError("[CharacterDataManager] CharacterData is null.");
            return;
        }

        m_CharacterData = characterData;
        Debug.Log($"[CharacterDataManager] CharacterData set: {m_CharacterData}");

        OnChanged?.Invoke();
    }

    public void SetMonsterData(MonsterData monsterData)
    {
        if (monsterData == null)
        {
            Debug.LogError("[CharacterDataManager] MonsterData is null.");
            return;
        }

        m_MonsterData = monsterData;
        Debug.Log($"[CharacterDataManager] MonsterData set: {m_MonsterData}");

        OnChanged?.Invoke();
    }

    public void EquipWeapon(WeaponData weaponData)
    {
        if (weaponData == null)
        {
            Debug.LogError("[CharacterDataManager] WeaponData is null! Cannot equip.");
            return;
        }

        if (m_CharacterData == null)
        {
            Debug.LogError("[CharacterDataManager] CharacterData is null! Cannot equip weapon.");
            return;
        }

        m_EquippedWeapon = weaponData;
        m_CharacterData.EquipWeapon(weaponData);

        OnChanged?.Invoke();
    }

    public void UnequipWeapon()
    {
        if (m_CharacterData == null)
        {
            Debug.LogError("[CharacterDataManager] CharacterData is null! Cannot unequip weapon.");
            return;
        }

        if (m_EquippedWeapon == null)
        {
            Debug.LogWarning("[CharacterDataManager] No weapon is currently equipped.");
            return;
        }

        m_EquippedWeapon = null;
        m_CharacterData.UnequipWeapon();

        OnChanged?.Invoke();
    }

    /// <summary>
    /// 스텟 조정(현재는 string 기반이지만, 추후 enum으로 바꾸는 걸 추천)
    /// </summary>
    public void AdjustStat(string statName, int value)
    {
        if (m_CharacterData == null)
        {
            Debug.LogError("[CharacterDataManager] No CharacterData is set!");
            return;
        }

        switch (statName)
        {
            case "Health":
                m_CharacterData.maxHealth += value;
                break;
            case "AttackPower":
                m_CharacterData.attackPower += value;
                break;
            default:
                Debug.LogWarning($"[CharacterDataManager] Stat '{statName}' not recognized.");
                break;
        }

        OnChanged?.Invoke();
    }
}
