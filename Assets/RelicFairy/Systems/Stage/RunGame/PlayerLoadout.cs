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

    // 선택된 유물 클래스 (CombatGirl 단일 몸에 적용). 무기와 독립.
    public RelicClassSO Relic { get; private set; }

    // 슬롯 0 = 메인 무기 (공격 + E/R 스킬)
    public WeaponSO WeaponSlot0 { get; private set; }
    // 슬롯 1 = 서브 장비 (Q 스킬 전용)
    public WeaponSO WeaponSlot1 { get; private set; }

    // CombatGirl 단일 몸 체제: CharacterData 없이 body 키만 있어도 준비 완료(무기 픽업 허용).
    public bool IsReady => CharacterData != null || !string.IsNullOrEmpty(CharacterPrefabKey);

    public void SetCharacter(CharacterData data, string prefabKey)
    {
        CharacterData    = data;
        CharacterPrefabKey = prefabKey;
    }

    public void SetRelic(RelicClassSO relic) => Relic = relic;

    public void SetWeaponSlot0(WeaponSO weapon) => WeaponSlot0 = weapon;
    public void SetWeaponSlot1(WeaponSO weapon) => WeaponSlot1 = weapon;

    public void Clear()
    {
        CharacterData      = null;
        CharacterPrefabKey = null;
        Relic              = null;
        WeaponSlot0        = null;
        WeaponSlot1        = null;
    }
}
