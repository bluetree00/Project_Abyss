using System.Collections.Generic;

public sealed class StagePointContext
{
    public int PointId { get; }
    public StageCategory StageCategory { get; }
    public IReadOnlyList<int> NextPointIds { get; }

    // Normal 노드에서만 의미 있음
    public NormalRoomCategory NormalRoomCategory { get; }

    // 필터(선택)
    public int? MinDifficulty { get; private set; }
    public int? MaxDifficulty { get; private set; }
    public IReadOnlyList<string> RequiredTags { get; private set; }

    public StagePointState State { get; private set; } = StagePointState.Locked;

    public bool IsResolved { get; private set; }
    public string ResolvedRoomId { get; private set; }

    public StagePointContext(
        int pointId,
        StageCategory stageCategory,
        IReadOnlyList<int> nextPointIds,
        NormalRoomCategory normalRoomCategory)
    {
        PointId = pointId;
        StageCategory = stageCategory;
        NextPointIds = nextPointIds;
        NormalRoomCategory = normalRoomCategory;
    }

    public void SetFilters(int? min, int? max, IReadOnlyList<string> tags)
    {
        MinDifficulty = min;
        MaxDifficulty = max;
        RequiredTags = tags;
    }

    public void SetState(StagePointState state) => State = state;

    public void SetResolvedRoomId(string roomId)
    {
        ResolvedRoomId = roomId;
        IsResolved = !string.IsNullOrEmpty(roomId);
    }
}
