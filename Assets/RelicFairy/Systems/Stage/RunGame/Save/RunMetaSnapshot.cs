using System.Collections.Generic;

/// <summary>
/// 절차생성(RunFlowController/RunSequencer)의 진행 상태 스냅샷.
/// 방 경계 저장 시 RunFlowController가 채워 RunProgressManager에 전달한다.
/// 이어하기 시 이 값으로 시퀀서를 복원하고 저장된 방을 동일 시드로 재생성한다.
/// </summary>
public struct RunMetaSnapshot
{
    public int masterSeed;          // 런 마스터 시드
    /// <summary>현재 챕터의 시퀀서 시드. Ch1=마스터 시드, Ch2+=마스터 시드에서 파생.
    /// 0이면 구버전 세이브 → 복원 시 마스터 시드+챕터로 재계산(무손실 폴백).</summary>
    public int chapterSeed;
    public int visitCount;          // RunSequencer 진행 방 수
    public int seqPhase;            // RunSequencer.Phase (int)
    // 특수방 챕터 캡 소모(= 실제로 방문한 횟수). 4종 전부 저장해야 이어하기로 캡이 리셋되지 않는다.
    public int shopUsed;
    public int eventUsed;
    public int crucibleUsed;
    public int refineryUsed;

    // PRD 미출현 누적 — 안 만나거나 지나칠수록 다음 방 등장 확률이 오른다. 복원 안 하면 기대치가 초기화된다.
    public int shopMiss;
    public int eventMiss;
    public int crucibleMiss;
    public int refineryMiss;

    public int heading;             // 현재 방 진입 방향(DoorEdge int)
    public int anchorToggle;        // 리프프로그 앵커 토글

    public string currentRoomPoolKey;   // 현재 방 pool_key (재생성 대상)
    public int    currentRoomKind;      // RoomPlanKind (int)
    public int    currentRoomMirror;    // 좌우 미러 (0/1)

    /// <summary>현재 방을 이미 클리어했는가. 복원 시 true면 몬스터를 재스폰하지 않고 출구를 즉시 개방한다.
    /// (클리어 후 저장을 허용하려면 필수 — 없으면 보상은 챙긴 채 몹이 부활해 중복 파밍이 된다.)</summary>
    public bool currentRoomCleared;

    /// <summary>클리어 보상 오브젝트가 스폰됐지만 아직 [F]로 받지 않았는가.
    /// 복원 시 true일 때만 보상을 다시 세운다(false면 이미 수령 → 재지급 금지).</summary>
    public bool currentRoomRewardPending;

    /// <summary>이 방에서 재련소가 소비한 결정적 롤 수. 복원 시 그만큼 RNG 스트림을 진행시켜
    /// "재접속하면 같은 롤을 다시 굴리는" save-scum을 막는다.</summary>
    public int crucibleRollIndex;

    public List<CooldownKV> cooldowns;  // RunSequencer 쿨다운
}
