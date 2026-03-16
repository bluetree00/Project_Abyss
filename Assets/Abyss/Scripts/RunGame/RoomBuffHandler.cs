using System.Collections.Generic;

/// <summary>
/// 활성 중인 방 버프 하나의 런타임 데이터
/// </summary>
public sealed class ActiveRoomBuff
{
    public StatModifier Modifier;
    public int RoomsRemaining;   // 0이 되면 자동 제거

    public ActiveRoomBuff(StatModifier modifier, int roomDuration)
    {
        Modifier       = modifier;
        RoomsRemaining = roomDuration;
    }
}

/// <summary>
/// 방(Room) 단위 버프 관리자
///
/// ■ 버프 추가
///   AddBuff(modifier, roomDuration)
///   - roomDuration: 앞으로 몇 방을 지속할지 (1 = 현재 방만, 4 = 이번 방 포함 4방)
///
/// ■ 방 퇴장 시
///   OnRoomExit() — 모든 버프의 남은 방 수를 1 감소, 0이 된 버프는 자동 제거
///
/// ■ 스탯 연동
///   OnBuffsChanged 이벤트 → PlayerRuntimeStats.RefreshRoomBuffs() 구독
/// </summary>
public sealed class RoomBuffHandler
{
    private readonly List<ActiveRoomBuff> _activeBuffs = new List<ActiveRoomBuff>();

    /// <summary>버프 목록이 바뀔 때 발생 — 구독자가 스탯을 재계산</summary>
    public event System.Action OnBuffsChanged;

    public IReadOnlyList<ActiveRoomBuff> ActiveBuffs => _activeBuffs;

    // ── 버프 추가 ─────────────────────────────────────────────
    /// <summary>
    /// 버프를 추가합니다.
    /// </summary>
    /// <param name="modifier">적용할 스탯 수정자</param>
    /// <param name="roomDuration">지속 방 수 (1 이상)</param>
    public void AddBuff(StatModifier modifier, int roomDuration)
    {
        if (roomDuration <= 0) return;
        _activeBuffs.Add(new ActiveRoomBuff(modifier, roomDuration));
        OnBuffsChanged?.Invoke();
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

    // ── 스탯 합산 ─────────────────────────────────────────────
    public float GetTotal(StatType type)
    {
        float total = 0f;
        foreach (var buff in _activeBuffs)
            if (buff.Modifier.Type == type) total += buff.Modifier.Value;
        return total;
    }
}
