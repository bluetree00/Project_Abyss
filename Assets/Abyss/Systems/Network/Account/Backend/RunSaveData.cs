using System;
using System.Collections.Generic;

/// <summary>
/// 이어하기 저장 데이터 DTO.
/// 뒤끝 RUN_PROGRESS 테이블 컬럼과 1:1 대응하며, JSON 직렬화 필드는
/// SavedStageGraph / ItemListWrapper / SynergyListWrapper 로 분리된다.
/// </summary>
[Serializable]
public class RunSaveData
{
    public int    slotIndex;          // 0, 1, 2 — 저장 슬롯 번호
    public bool   hasActiveRun;
    public int    chapter;           // ChapterId enum 값
    public int    currentPointId;    // StagePointManager.CurrentPointId
    public int    currentHp;
    public int    maxHp;
    public int    runGold;
    public int    retryCount;        // 이 슬롯에서 새 런을 시작한 누적 횟수
    public int    progressPercent;   // 0-100 — 챕터 기반 진행도
    public string characterKey;      // PlayerLoadout.CharacterPrefabKey
    public string characterName;     // CharacterData.characterName (표시용)
    public string weapon0PrefabKey;  // WeaponData.weaponPrefabKey (슬롯 0)
    public string weapon1PrefabKey;  // WeaponData.weaponPrefabKey (슬롯 1)
    public int    itemCount;          // 현재 보유 아이템 수 (빠른 표시용)
    public int    synergyCount;       // 현재 활성 시너지 수 (빠른 표시용)
    public int    roomClearCount;     // 이 런에서 클리어한 방 수
    public string graphJson;         // SavedStageGraph JSON
    public string itemsJson;         // ItemListWrapper JSON
    public string synergiesJson;     // SynergyListWrapper JSON
    public string roomLogsJson;           // RoomClearLogWrapper JSON
    public string savedAt;                // ISO8601 UTC
    public bool   isInStartRoom;          // true = 스타트룸 미퇴장 상태 (이어하기 시 StartRoom 재진입)
    public int    currentZoneIndex;       // ZoneProgressionService.CurrentZoneIndex (zone-layout 모드 이어하기)
    public string clearedZoneIndicesJson; // IntListWrapper JSON — 클리어된 존 인덱스 목록
}

/// <summary>
/// StageMapGraph + 각 노드의 StagePointContext 상태를 합쳐서 직렬화한다.
/// 이어하기 시 이 데이터만으로 그래프를 재생성 없이 완전 복원 가능.
/// </summary>
[Serializable]
public sealed class SavedStageGraph
{
    public int[]           fullPattern;
    public int[]           middlePattern;
    public SavedStageNode[] nodes;
}

[Serializable]
public sealed class SavedStageNode
{
    // StageMapNode 정보
    public int   pointId;
    public int   stageCategory;        // StageCategory as int
    public int   normalRoomCategory;   // NormalRoomCategory as int
    public int   layerIndex;
    public int   indexInLayer;
    public int[] nextPointIds;

    // StagePointContext 상태
    public int    state;               // StagePointState as int
    public string resolvedRoomId;
    public bool   isResolved;
    public int    minDifficulty;       // -1 = null
    public int    maxDifficulty;       // -1 = null
}

// ── JsonUtility 직렬화용 래퍼 ──
// JsonUtility.ToJson은 최상위 List<T>를 직렬화하지 못해 래퍼 클래스를 사용한다.

[Serializable]
public sealed class ItemListWrapper
{
    public List<RuntimeItemData> items = new();
}

[Serializable]
public sealed class IntListWrapper
{
    public List<int> items = new();
}

[Serializable]
public sealed class SynergyListWrapper
{
    public List<SynergyRecord> items = new();
}
