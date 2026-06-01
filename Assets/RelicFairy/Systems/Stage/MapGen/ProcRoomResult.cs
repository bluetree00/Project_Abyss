using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 격리형 절차 룸 빌드 결과. GameRunBootstrapper.BuildProcRoomAsync가 반환한다.
/// RunFlowController가 진입 위치로 플레이어를 이동시키고, 클리어 후 exits에 게이트를 배치한다.
/// </summary>
public sealed class ProcRoomResult
{
    public GameObject         roomGO;   // 빌드된 방 루트 (디스폰 시 사용)
    public Vector3            entryPos; // 플레이어 진입(등장) 위치
    public List<ProcExitSlot> exits;    // 클리어 후 ProcRoomGate를 놓을 출구 슬롯
}

/// <summary>출구 슬롯 — 게이트 배치 위치 + 직진/턴 여부 + 문 엣지(전환 와이프 방향용).</summary>
public struct ProcExitSlot
{
    public Vector3  worldPos;
    public bool     isForward; // true=직진(North), false=턴(East/West)
    public DoorEdge edge;      // North=직진(위) / East=우턴 / West=좌턴 — 전환 와이프 방향
}
