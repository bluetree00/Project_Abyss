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

    // 둘째 시작 파츠 — 기억의 제단 「시작 파츠 둘」을 열었을 때만(09-29). Clear()에서 리셋.
    public string StartPartId2 { get; private set; }

    // 보스 클리어 드래프트로 이번 런에 획득한 유물 기억 조각 id(런 내 성장). Clear()에서 리셋.
    private readonly List<string> _relicPartIds = new();
    public IReadOnlyList<string> RelicPartIds => _relicPartIds;

    // 유물 성장 v2(10-02) — 조각마다 등급(드러난 줄 수) · 공명 메아리(시간대 · 계단별) · 천장 · 다시 떠올리기.
    // 모두 런 안에서만 산다(힘은 런 안에서만 — D8). SetRelic 교체 · Clear()에서 함께 비운다.
    private readonly Dictionary<string, RelicMemoryGrade> _relicPartGrades = new();
    private readonly Dictionary<string, int> _relicEchoes = new();

    /// <summary>찬란이 나오지 않은 드래프트 수(천장 — <see cref="RelicMemoryOdds.PityDrafts"/>).</summary>
    public int RelicDraftsSinceRadiant { get; private set; }

    /// <summary>이번 런에 「다시 떠올리기」(제단 노드, 런당 1회)를 썼는가.</summary>
    public bool RelicRedrawUsed { get; private set; }

    /// <summary>유물 파츠가 늘었다(드래프트 · 계승 · 복원) — 발동 계열 각인을 다시 센다(09-29).</summary>
    [field: NonSerialized] public event Action RelicPartsChanged;

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
        if (changed) ClearRelicMemory();
        EvaluateFirstRun();
    }

    public void SetWeaponSlot0(WeaponSO weapon) => WeaponSlot0 = weapon;

    public void SetWeaponSlot1(WeaponSO weapon)
    {
        WeaponSlot1 = weapon;
        EvaluateFirstRun();
    }

    public void SetStartPart(string partId) => StartPartId = partId;

    public void SetStartPart2(string partId) => StartPartId2 = partId;

    /// <summary>
    /// 유물과 원거리 무기가 <b>둘 다</b> 정해지는 순간 초행을 판정한다.
    /// 종료 시점에 보면 그 사이 기록이 쓰여 "방금 한 것" 때문에 초행이 아니게 된다 — 확정 시 1회다.
    /// </summary>
    private void EvaluateFirstRun()
    {
        if (Relic == null || WeaponSlot1 == null) return;
        FirstRunService.MarkLoadout(Relic.Id.ToString(), WeaponSlot1.name);
    }

    /// <summary>[옛 호출 호환] 흐릿으로 얻는다.</summary>
    public void AddRelicPart(string partId) => AddRelicPart(partId, RelicMemoryGrade.Faint);

    /// <summary>보스 클리어 드래프트가 호출. 중복 part_id는 무시(이미 가진 조각은 「선명하게」로 올린다).</summary>
    public void AddRelicPart(string partId, RelicMemoryGrade grade)
    {
        if (string.IsNullOrEmpty(partId) || _relicPartIds.Contains(partId)) return;

        _relicPartIds.Add(partId);
        _relicPartGrades[partId] = grade < RelicMemoryGrade.Faint ? RelicMemoryGrade.Faint : grade;
        RelicPartsChanged?.Invoke();

        // 초행 보너스는 <b>코어 파츠</b> 전용이다(기능 파츠 4종은 대상이 아니다).
        // part_kind의 정본은 차트다 — 데이터를 못 읽으면 주지 않는다(과지급보다 미지급이 안전).
        // (에디터 모드 실측에서 Managers.Instance가 씬에 @Managers를 만들지 않게 플레이 중에만 본다)
        if (Application.isPlaying && Managers.RelicParts?.GetById(partId)?.part_kind == CorePartKind)
            FirstRunService.MarkCorePart(partId);
    }

    /// <summary>
    /// 이어하기 복원 — 저장된 조각(<c>id:등급</c>, 쉼표)과 덤 칸(메아리 · 천장 · 다시 떠올리기)을 되살린다.
    /// 등급이 없는 옛 저장은 흐릿, 데이터에 없는 id(옛 v1 파츠 등)는 버린다 — 이어하기가 예외로 멈추면 안 된다.
    /// 초행 보너스는 획득 시점에 이미 기록됐으므로 여기서는 세지 않는다.
    /// <b>유물 복원 뒤에</b> 불러야 한다 — <see cref="SetRelic"/>이 파츠를 비운다.
    /// </summary>
    public void RestoreRelicParts(string savedIds, string savedExtra = null)
    {
        if (!string.IsNullOrEmpty(savedIds))
        {
            foreach (var raw in savedIds.Split(','))
            {
                var token = raw.Trim();
                if (token.Length == 0) continue;
                int colon = token.IndexOf(':');
                var id = colon >= 0 ? token.Substring(0, colon) : token;
                var grade = RelicMemoryGrade.Faint;
                if (colon >= 0 && int.TryParse(token.Substring(colon + 1), out int g) && g >= 1 && g <= 3)
                    grade = (RelicMemoryGrade)g;
                if (id.Length == 0 || _relicPartIds.Contains(id)) continue;
                var parts = Application.isPlaying ? Managers.RelicParts : null;
                if (parts != null && parts.IsInitialized && parts.GetById(id) == null)
                {
                    Debug.Log($"[PlayerLoadout] 저장된 유물 조각 「{id}」가 지금 데이터에 없다 — 버린다(옛 v1 파츠)");
                    continue;
                }
                _relicPartIds.Add(id);
                _relicPartGrades[id] = grade;
            }
        }

        if (!string.IsNullOrEmpty(savedExtra))
        {
            foreach (var raw in savedExtra.Split(';'))
            {
                int eq = raw.IndexOf('=');
                if (eq <= 0) continue;
                var key = raw.Substring(0, eq).Trim();
                if (!int.TryParse(raw.Substring(eq + 1), out int v)) continue;
                if (key.StartsWith("echo.")) { if (v > 0) _relicEchoes[key.Substring(5)] = v; }
                else if (key == "pity")   RelicDraftsSinceRadiant = Mathf.Max(0, v);
                else if (key == "redraw") RelicRedrawUsed = v != 0;
            }
        }
        RelicPartsChanged?.Invoke();
    }

    /// <summary>저장용 조각 문자열 — <c>id:등급,…</c>.</summary>
    public string SerializeRelicParts()
    {
        if (_relicPartIds.Count == 0) return string.Empty;
        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < _relicPartIds.Count; i++)
        {
            if (i > 0) sb.Append(',');
            var id = _relicPartIds[i];
            sb.Append(id).Append(':').Append((int)GetRelicPartGrade(id));
        }
        return sb.ToString();
    }

    /// <summary>저장용 덤 칸 — <c>echo.dawn=1;pity=2;redraw=1</c>.</summary>
    public string SerializeRelicExtra()
    {
        var sb = new System.Text.StringBuilder();
        foreach (var kv in _relicEchoes)
            if (kv.Value > 0) sb.Append("echo.").Append(kv.Key).Append('=').Append(kv.Value).Append(';');
        if (RelicDraftsSinceRadiant > 0) sb.Append("pity=").Append(RelicDraftsSinceRadiant).Append(';');
        if (RelicRedrawUsed) sb.Append("redraw=1;");
        return sb.ToString();
    }

    /// <summary>이미 보유한 파츠인지 — 드래프트 후보 중복 배제에 사용.</summary>
    public bool HasRelicPart(string partId) => _relicPartIds.Contains(partId);

    /// <summary>보유 조각의 등급(없으면 None).</summary>
    public RelicMemoryGrade GetRelicPartGrade(string partId)
        => !string.IsNullOrEmpty(partId) && _relicPartGrades.TryGetValue(partId, out var g) ? g
         : partId != null && _relicPartIds.Contains(partId) ? RelicMemoryGrade.Faint : RelicMemoryGrade.None;

    /// <summary>「선명하게」 — 보유 조각을 한 단 올린다(다음 줄이 열린다). 찬란이거나 없으면 false.</summary>
    public bool RaiseRelicPartGrade(string partId)
    {
        var g = GetRelicPartGrade(partId);
        if (g == RelicMemoryGrade.None || g >= RelicMemoryGrade.Radiant) return false;
        _relicPartGrades[partId] = g + 1;
        RelicPartsChanged?.Invoke();
        return true;
    }

    /// <summary>공명 메아리(채우기 사슬) — 시간대(가웨인)나 계단(랜슬롯) 하나에 +1.</summary>
    public int GetRelicEcho(string anchor)
        => !string.IsNullOrEmpty(anchor) && _relicEchoes.TryGetValue(anchor, out int n) ? n : 0;

    public void AddRelicEcho(string anchor)
    {
        if (string.IsNullOrEmpty(anchor)) return;
        _relicEchoes[anchor] = GetRelicEcho(anchor) + 1;
        RelicPartsChanged?.Invoke();
    }

    /// <summary>드래프트 한 번이 끝났다 — 찬란을 받았는지로 천장 카운터를 올리거나 비운다.</summary>
    public void NoteRelicDraft(bool hadRadiant)
        => RelicDraftsSinceRadiant = hadRadiant ? 0 : RelicDraftsSinceRadiant + 1;

    /// <summary>「다시 떠올리기」를 이번 런에 썼다.</summary>
    public void MarkRelicRedrawUsed() => RelicRedrawUsed = true;

    private void ClearRelicMemory()
    {
        _relicPartIds.Clear();
        _relicPartGrades.Clear();
        _relicEchoes.Clear();
        RelicDraftsSinceRadiant = 0;
        RelicRedrawUsed = false;
    }

    public void Clear()
    {
        CharacterData      = null;
        CharacterPrefabKey = null;
        Relic              = null;
        WeaponSlot0        = null;
        WeaponSlot1        = null;
        StartPartId        = null;
        StartPartId2       = null;
        ClearRelicMemory();
    }
}
