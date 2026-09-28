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
    // RELIC_PARTS_DATA의 part_kind 값 — 초행 보너스 대상 판정용.
    private const string CorePartKind = "core";

    public CharacterData CharacterData { get; private set; }
    public string CharacterPrefabKey { get; private set; }

    // 선택된 유물 클래스 (CombatGirl 단일 몸에 적용). 무기와 독립.
    public RelicClassSO Relic { get; private set; }

    // 슬롯 0 = 메인 무기 (공격 + E/R 스킬)
    public WeaponSO WeaponSlot0 { get; private set; }
    // 슬롯 1 = 서브 장비 (Q 스킬 전용)
    public WeaponSO WeaponSlot1 { get; private set; }

    // 베이스캠프 파츠 공방에서 고른 시작 원거리 파츠 id — 런 시작에 Lv1로 켠다(GameRunSession). Clear()에서 리셋.
    public string StartPartId { get; private set; }

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
        EvaluateFirstRun();
    }

    public void SetWeaponSlot0(WeaponSO weapon) => WeaponSlot0 = weapon;

    public void SetWeaponSlot1(WeaponSO weapon)
    {
        WeaponSlot1 = weapon;
        EvaluateFirstRun();
    }

    public void SetStartPart(string partId) => StartPartId = partId;

    /// <summary>
    /// 유물과 원거리 무기가 <b>둘 다</b> 정해지는 순간 초행을 판정한다.
    /// 종료 시점에 보면 그 사이 기록이 쓰여 "방금 한 것" 때문에 초행이 아니게 된다 — 확정 시 1회다.
    /// </summary>
    private void EvaluateFirstRun()
    {
        if (Relic == null || WeaponSlot1 == null) return;
        FirstRunService.MarkLoadout(Relic.Id.ToString(), WeaponSlot1.name);
    }

    /// <summary>보스 클리어 드래프트가 호출. 중복 part_id는 무시.</summary>
    public void AddRelicPart(string partId)
    {
        if (string.IsNullOrEmpty(partId) || _relicPartIds.Contains(partId)) return;

        _relicPartIds.Add(partId);

        // 초행 보너스는 <b>코어 파츠</b> 전용이다(기능 파츠 4종은 대상이 아니다).
        // part_kind의 정본은 차트다 — 데이터를 못 읽으면 주지 않는다(과지급보다 미지급이 안전).
        if (Managers.RelicParts?.GetById(partId)?.part_kind == CorePartKind)
            FirstRunService.MarkCorePart(partId);
    }

    /// <summary>
    /// 이어하기 복원 — 저장된 파츠 id(쉼표)를 되살린다.
    /// 초행 보너스는 획득 시점에 이미 기록됐으므로 여기서는 세지 않는다.
    /// <b>유물 복원 뒤에</b> 불러야 한다 — <see cref="SetRelic"/>이 파츠를 비운다.
    /// </summary>
    public void RestoreRelicParts(string savedIds)
    {
        if (string.IsNullOrEmpty(savedIds)) return;

        foreach (var raw in savedIds.Split(','))
        {
            var id = raw.Trim();
            if (id.Length == 0 || _relicPartIds.Contains(id)) continue;
            _relicPartIds.Add(id);
        }
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
        StartPartId        = null;
        _relicPartIds.Clear();
    }
}
