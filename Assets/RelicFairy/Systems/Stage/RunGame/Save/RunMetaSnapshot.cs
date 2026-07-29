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

    /// <summary>현재 방을 이미 클리어했는가. 복원 시 true면 몬스터를 재스폰하지 않고 출구를 즉시 개방한다.
    /// (클리어 후 저장을 허용하려면 필수 — 없으면 보상은 챙긴 채 몹이 부활해 중복 파밍이 된다.)</summary>
    public bool currentRoomCleared;

    /// <summary>이 방에서 재련소가 소비한 결정적 롤 수. 복원 시 그만큼 RNG 스트림을 진행시켜
    /// "재접속하면 같은 롤을 다시 굴리는" save-scum을 막는다.</summary>
    public int crucibleRollIndex;

    public List<CooldownKV> cooldowns;  // RunSequencer 쿨다운
}
