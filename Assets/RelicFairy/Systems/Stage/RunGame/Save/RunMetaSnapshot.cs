using System.Collections.Generic;

/// <summary>
/// 절차생성(RunFlowController/RunSequencer)의 진행 상태 스냅샷.
/// 방 경계 저장 시 RunFlowController가 채워 RunProgressManager에 전달한다.
/// 이어하기 시 이 값으로 시퀀서를 복원하고 저장된 방을 동일 시드로 재생성한다.
/// </summary>
public struct RunMetaSnapshot
{
    public int masterSeed;          // 런 마스터 시드
    public int visitCount;          // RunSequencer 진행 방 수
    public int seqPhase;            // RunSequencer.Phase (int)
    public int shopUsed;
    public int eventUsed;
    public int heading;             // 현재 방 진입 방향(DoorEdge int)
    public int anchorToggle;        // 리프프로그 앵커 토글

    public string currentRoomPoolKey;   // 현재 방 pool_key (재생성 대상)
    public int    currentRoomKind;      // RoomPlanKind (int)
    public int    currentRoomMirror;    // 좌우 미러 (0/1)

    public List<CooldownKV> cooldowns;  // RunSequencer 쿨다운
}
