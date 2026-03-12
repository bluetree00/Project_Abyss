using System;
using UnityEngine;

public sealed class CharacterDataManager
{
    private static CharacterData m_CharacterData;
    public CharacterData M_CharacterData => m_CharacterData;

    private static MonsterData m_MonsterData;
    public MonsterData M_MonsterData => m_MonsterData;

    /// <summary>
    /// PrepPanel에서 선택한 캐릭터의 Addressable 프리팹 키.
    /// GameRunBootstrapper가 플레이어 스폰 시 이 값을 우선 사용합니다.
    /// </summary>
    public string PlayerPrefabKey { get; private set; }

    /// <summary>
    /// 캐릭터/무기/스탯 등 HUD/UI가 갱신되어야 하는 변화가 발생했을 때 호출
    /// </summary>
    public event Action OnChanged;

    public void SetCharacterData(CharacterData characterData, string prefabKey = null)
    {
        if (characterData == null)
        {
            Debug.LogError("[CharacterDataManager] CharacterData is null.");
            return;
        }

        m_CharacterData  = characterData;
        PlayerPrefabKey  = prefabKey;
        Debug.Log($"[CharacterDataManager] CharacterData set: {m_CharacterData}, prefabKey: {prefabKey}");

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

}
