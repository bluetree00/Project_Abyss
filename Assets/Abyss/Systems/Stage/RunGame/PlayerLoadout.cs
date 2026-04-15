using System;
using UnityEngine;

/// <summary>
/// 로비 준비 화면에서 확정된 캐릭터 + 무기 로드아웃.
/// AppBootstrapper(DDOL)에 보관되어 InGame 씬으로 전달됩니다.
/// </summary>
[Serializable]
public class PlayerLoadout
{
    public CharacterData CharacterData { get; private set; }
    public string CharacterPrefabKey { get; private set; }

    // 슬롯 0 = 메인 무기 (공격 + E/R 스킬)
    public WeaponSO WeaponSlot0 { get; private set; }
    // 슬롯 1 = 서브 장비 (Q 스킬 전용)
    public WeaponSO WeaponSlot1 { get; private set; }

    public bool IsReady => CharacterData != null;

    public void SetCharacter(CharacterData data, string prefabKey)
    {
        CharacterData    = data;
        CharacterPrefabKey = prefabKey;
    }

    public void SetWeaponSlot0(WeaponSO weapon) => WeaponSlot0 = weapon;
    public void SetWeaponSlot1(WeaponSO weapon) => WeaponSlot1 = weapon;

    public void Clear()
    {
        CharacterData      = null;
        CharacterPrefabKey = null;
        WeaponSlot0        = null;
        WeaponSlot1        = null;
    }
}
