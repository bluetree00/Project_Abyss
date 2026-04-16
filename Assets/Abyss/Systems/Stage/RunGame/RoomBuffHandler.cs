using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 활성 중인 방 버프 하나의 런타임 데이터
/// </summary>
public sealed class ActiveRoomBuff
{
    public StatModifier Modifier;
    public int RoomsRemaining;   // 0이 되면 자동 제거
    public bool IsPercent;       // true면 기본 스탯의 %로 적용
    public bool IsDebuff;        // 디버프 여부 (해제 확률 판정용)
    public string BuffType;      // 테이블 buff_type (MoveSpeed, AttackPower 등)
    public int Tier;             // 1, 2, 3
    public int BaseDuration;     // 원래 지속 방 수 (리셋용)

    public ActiveRoomBuff(StatModifier modifier, int roomDuration, bool isPercent = false, bool isDebuff = false,
                          string buffType = null, int tier = 1)
    {
        Modifier       = modifier;
        RoomsRemaining = roomDuration;
        BaseDuration   = roomDuration;
        IsPercent      = isPercent;
        IsDebuff       = isDebuff;
        BuffType       = buffType ?? "";
        Tier           = tier;
    }
}

/// <summary>
/// 방(Room) 단위 버프 관리자
///
/// ■ 버프 추가
///   AddBuff(modifier, roomDuration, isPercent, isDebuff)
///   - 슬롯 초과 시 가장 오래된 버프부터 삭제
///
/// ■ 방 퇴장 시
///   OnRoomExit() — 모든 버프의 남은 방 수를 1 감소, 0이 된 버프는 자동 제거
///
/// ■ 디버프 해제
///   TryRemoveDebuff() — 10% 확률로 디버프 1개 해제
///
/// ■ 스탯 연동
///   OnBuffsChanged 이벤트 → PlayerRuntimeStats.RefreshRoomBuffs() 구독
/// </summary>
public sealed class RoomBuffHandler
{
    private const int DefaultMaxSlots = 10;
    private const float DebuffRemoveChance = 0.10f;  // 디버프 해제 확률 10%
    private const float BuffRemoveChance   = 0.90f;  // 버프 해제 확률 90%

    private readonly List<ActiveRoomBuff> _activeBuffs = new List<ActiveRoomBuff>();
    private int _maxSlots = DefaultMaxSlots;

    /// <summary>버프 목록이 바뀔 때 발생 — 구독자가 스탯을 재계산</summary>
    public event System.Action OnBuffsChanged;

    public IReadOnlyList<ActiveRoomBuff> ActiveBuffs => _activeBuffs;
    public int MaxSlots => _maxSlots;
    public int BuffCount => _activeBuffs.Count;

    /// <summary>최대 슬롯 수 변경.</summary>
    public void SetMaxSlots(int slots)
    {
        _maxSlots = UnityEngine.Mathf.Max(1, slots);
        EnforceSlotLimit();
    }

    // ── 버프 추가 ─────────────────────────────────────────────

    /// <summary>
    /// 버프를 추가합니다. 슬롯 초과 시 가장 오래된 버프부터 삭제.
    /// </summary>
    public void AddBuff(StatModifier modifier, int roomDuration, bool isPercent = false, bool isDebuff = false,
                        string buffType = null, int tier = 1)
    {
        if (roomDuration <= 0) return;
        _activeBuffs.Add(new ActiveRoomBuff(modifier, roomDuration, isPercent, isDebuff, buffType, tier));
        EnforceSlotLimit();
        OnBuffsChanged?.Invoke();
    }

    // ── 디버프 해제 시도 ──────────────────────────────────────

    /// <summary>
    /// 버프/디버프 해제를 시도합니다.
    /// 버프: 90% 확률로 해제, 디버프: 10% 확률로 해제.
    /// </summary>
    /// <returns>해제 성공 여부</returns>
    public bool TryRemoveBuff(int index)
    {
        if (index < 0 || index >= _activeBuffs.Count) return false;

        var buff = _activeBuffs[index];
        float chance = buff.IsDebuff ? DebuffRemoveChance : BuffRemoveChance;

        if (UnityEngine.Random.value > chance) return false;

        _activeBuffs.RemoveAt(index);
        OnBuffsChanged?.Invoke();
        return true;
    }

    // ── 방 퇴장 ───────────────────────────────────────────────

    /// <summary>
    /// 방을 떠날 때 호출. 모든 버프의 남은 방 수를 1 감소시키고
    /// 만료된 버프를 제거합니다.
    /// </summary>
    public void OnRoomExit()
    {
        bool changed = false;
        for (int i = _activeBuffs.Count - 1; i >= 0; i--)
        {
            _activeBuffs[i].RoomsRemaining--;
            if (_activeBuffs[i].RoomsRemaining <= 0)
            {
                _activeBuffs.RemoveAt(i);
                changed = true;
            }
        }
        if (changed) OnBuffsChanged?.Invoke();
    }

    // ── 전체 제거 (런 종료 등) ────────────────────────────────

    public void ClearAll()
    {
        if (_activeBuffs.Count == 0) return;
        _activeBuffs.Clear();
        OnBuffsChanged?.Invoke();
    }

    // ── 티어 승급 (보스 처치 시) ─────────────────────────────

    /// <summary>
    /// 모든 버프의 지속시간을 리셋하고 1티어 승급.
    /// BuffRefreshOnBoss 아이템에서 호출.
    /// </summary>
    public void RefreshAndUpgradeAll()
    {
        var buffData = Managers.BuffData;
        bool changed = false;

        for (int i = 0; i < _activeBuffs.Count; i++)
        {
            var buff = _activeBuffs[i];

            // 지속시간 리셋
            buff.RoomsRemaining = buff.BaseDuration;

            // 티어 승급
            if (buffData == null || string.IsNullOrEmpty(buff.BuffType)) continue;
            int maxTier = buffData.GetMaxTier(buff.BuffType);
            if (buff.Tier >= maxTier) continue;

            int newTier = buff.Tier + 1;
            var upgraded = buffData.Get(buff.BuffType, newTier);
            if (upgraded == null) continue;

            buff.Tier = newTier;
            buff.Modifier = new StatModifier(buff.Modifier.Type, upgraded.value);
            buff.IsPercent = upgraded.is_percent;
            changed = true;
        }

        if (changed) OnBuffsChanged?.Invoke();
    }

    // ── 스탯 합산 (가산값 — 비퍼센트) ────────────────────────

    /// <summary>해당 스탯의 가산(Flat) 버프 합계.</summary>
    public float GetFlatTotal(StatType type)
    {
        float total = 0f;
        foreach (var buff in _activeBuffs)
            if (buff.Modifier.Type == type && !buff.IsPercent)
                total += buff.Modifier.Value;
        return total;
    }

    /// <summary>해당 스탯의 퍼센트 버프 합계 (0.1 = +10%).</summary>
    public float GetPercentTotal(StatType type)
    {
        float total = 0f;
        foreach (var buff in _activeBuffs)
            if (buff.Modifier.Type == type && buff.IsPercent)
                total += buff.Modifier.Value;
        return total;
    }

    /// <summary>레거시 호환 — Flat + Percent 구분 없이 전체 합산.</summary>
    public float GetTotal(StatType type)
    {
        float total = 0f;
        foreach (var buff in _activeBuffs)
            if (buff.Modifier.Type == type) total += buff.Modifier.Value;
        return total;
    }

    // ── 내부 ──────────────────────────────────────────────────

    private void EnforceSlotLimit()
    {
        while (_activeBuffs.Count > _maxSlots)
            _activeBuffs.RemoveAt(0); // 가장 오래된 버프 삭제
    }
}
