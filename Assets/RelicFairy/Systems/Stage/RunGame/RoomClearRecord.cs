using System;
using System.Collections.Generic;

/// <summary>
/// 방 한 칸 클리어 시 기록되는 런 스냅샷.
/// GameRunSession._roomClearRecords에 누적, RunSaveData.roomLogsJson으로 직렬화된다.
/// </summary>
[Serializable]
public sealed class RoomClearRecord
{
    public int    pointId;
    public int    chapter;
    public string runState;             // GameRunSession.RunState.ToString()
    public int    hpAfter;
    public int    maxHp;
    public int    goldAfter;
    public int    goldGainedInRoom;
    public int    itemsGainedCount;
    public int    totalItemCount;
    public int    synergiesGainedCount;
    public int    totalSynergyCount;
    public string clearedAt;            // ISO8601 UTC
}

/// <summary>JsonUtility 직렬화용 래퍼 (최상위 List<T>는 직렬화 불가).</summary>
[Serializable]
public sealed class RoomClearLogWrapper
{
    public List<RoomClearRecord> records = new();
}
