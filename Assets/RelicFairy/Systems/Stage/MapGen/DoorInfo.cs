/// <summary>방 엣지에서의 문 방향. North=+Z(위) / South=-Z(아래) / East=+X / West=-X.</summary>
public enum DoorEdge
{
    North,
    East,
    South,
    West,
}

/// <summary>
/// grid_csv의 DR 토큰에서 추출한 문 앵커 메타.
/// MapDataLoader.Parse가 doorInfos 딕셔너리에 채운다.
/// 연결 파이프라인이 입구/직진/턴 역할을 부여하고 선택된 문만 개방한다.
/// </summary>
public struct DoorInfo
{
    /// <summary>문이 위치한 엣지(셀 위치로 추론).</summary>
    public DoorEdge edge;
    /// <summary>개구부 폭(셀 수). 앵커 셀을 중심으로 ±. 기본 3.</summary>
    public int width;
}
