using System;
using System.Collections.Generic;
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

    // 베이스캠프에서 픽업한 서약 id 예약. 던전 진입(핸들러 Initialize 후) 시 CovenantHandler.TryAdd로 적용.
    private readonly List<string> _reservedCovenants = new();
    public IReadOnlyList<string> ReservedCovenants => _reservedCovenants;

    // 보스 클리어 드래프트로 이번 런에 획득한 유물 파츠 id(개화 = 런 내 임시 성장). Clear()에서 리셋.
    private readonly List<string> _relicPartIds = new();
    public IReadOnlyList<string> RelicPartIds => _relicPartIds;

    // CombatGirl 단일 몸 체제: CharacterData 없이 body 키만 있어도 준비 완료(무기 픽업 허용).
    public bool IsReady => CharacterData != null || !string.IsNullOrEmpty(CharacterPrefabKey);

    public void SetCharacter(CharacterData data, string prefabKey)
    {
        CharacterData    = data;
        CharacterPrefabKey = prefabKey;
    }

    /// <summary>
    /// 유물 교체. 파츠(개화)는 <b>유물 전용</b>이라 유물이 바뀌면 반드시 비운다.
    /// (안 비우면 랜슬롯으로 얻은 파츠가 가웨인 런에 그대로 남아 드래프트·효과가 섞인다.)
    /// </summary>
    public void SetRelic(RelicClassSO relic)
    {
        bool changed = Relic != relic;
        Relic = relic;
        if (changed) _relicPartIds.Clear();
    }

    public void SetWeaponSlot0(WeaponSO weapon) => WeaponSlot0 = weapon;
    public void SetWeaponSlot1(WeaponSO weapon) => WeaponSlot1 = weapon;

    /// <summary>베이스캠프 서약 픽업이 호출. 중복 id는 무시.</summary>
    public void AddCovenant(string id)
    {
        if (!string.IsNullOrEmpty(id) && !_reservedCovenants.Contains(id))
            _reservedCovenants.Add(id);
    }

    /// <summary>보스 클리어 드래프트가 호출. 중복 part_id는 무시.</summary>
    public void AddRelicPart(string partId)
    {
        if (!string.IsNullOrEmpty(partId) && !_relicPartIds.Contains(partId))
            _relicPartIds.Add(partId);
    }

    /// <summary>이미 보유한 파츠인지 — 드래프트 후보 중복 배제에 사용.</summary>
    public bool HasRelicPart(string partId) => _relicPartIds.Contains(partId);

    public void Clear()
    {
        CharacterData      = null;
        CharacterPrefabKey = null;
        Relic              = null;
        WeaponSlot0        = null;
        WeaponSlot1        = null;
        _reservedCovenants.Clear();
        _relicPartIds.Clear();
    }
}
