using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 격리형 절차 룸 빌드 결과. GameRunBootstrapper.BuildProcRoomAsync가 반환한다.
/// RunFlowController가 진입 위치로 플레이어를 이동시키고, 클리어 후 exits에 게이트를 배치한다.
/// </summary>
public sealed class ProcRoomResult
{
    public GameObject                   roomGO;       // 빌드된 방 루트 (디스폰 시 사용)
    public Vector3                      entryPos;     // 플레이어 진입(등장) 위치 (개구부 안쪽)
    public bool                         hasEntrance;  // 입구 문 존재 여부 (시작방은 false)
    public bool                         hasCeiling;   // 팔레트 천장 유무 — 상공 부감 인트로 카메라가 천장 방을 스킵할지 판단
    public ProcExitSlot                 entrance;     // 들어온 입구 — 진입 후 잠금(봉인) 패널 배치용
    public List<ProcExitSlot>           exits;        // 클리어 후 공개할 출구 슬롯
    public List<MapBuilder.PlacedBlock> blocks;       // 디졸브 등장용 블록(빌드 시 렌더러 숨김 상태) — 화면 복귀 후 호출자가 재생
}

/// <summary>출구 슬롯 — 게이트 배치 위치 + 직진/턴 여부 + 문 엣지(전환 와이프 방향용) + 개구부 치수.</summary>
public struct ProcExitSlot
{
    public Vector3  worldPos;      // 문 셀 바닥 월드 좌표 (개구부 수평 중앙)
    public bool     isForward;     // true=직진(North), false=턴(East/West)
    public DoorEdge edge;          // North=직진(위) / East=우턴 / West=좌턴 — 전환 와이프 방향
    public float    openingWidth;  // 개구부 폭(월드 단위) = 문 width × cellSize — 게이트 마커 정렬용
    public float    openingHeight; // 개구부 높이(월드 단위) = 벽 높이 — 게이트 마커 수직 중앙 정렬용
}
