using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;

/// <summary>
/// 방 블록 등장 연출 전략. MapRoomEntry.entrance 필드로 선택.
/// 새 연출 추가 시 구현체 + MapEntranceRegistry switch 한 줄만 추가.
/// </summary>
public interface IMapEntrance
{
    /// <summary>블록을 최종 상태로 애니메이션시킴. 완료까지 await.</summary>
    UniTask PlayAsync(
        IReadOnlyList<MapBuilder.PlacedBlock> blocks,
        MapEntranceContext ctx,
        CancellationToken ct);
}

/// <summary>연출에 필요한 입력. 구조체로 복사 비용 최소화.</summary>
public readonly struct MapEntranceContext
{
    public readonly MapRoomEntry Entry;

    public MapEntranceContext(MapRoomEntry entry) { Entry = entry; }

    public float ScatterRange   => Entry != null && Entry.scatter_range   > 0f ? Entry.scatter_range   : 10f;
    public float ReturnDuration => Entry != null && Entry.return_duration > 0f ? Entry.return_duration : 1.2f;
}
