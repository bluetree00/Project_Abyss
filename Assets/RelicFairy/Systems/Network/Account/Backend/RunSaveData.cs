using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 이어하기 저장 데이터 DTO.
/// 뒤끝 RUN_PROGRESS 테이블 컬럼과 1:1 대응하며, JSON 직렬화 필드는
/// ItemListWrapper / SynergyListWrapper 로 분리된다.
///
/// PR1(로컬 세이브): 아래 "── 로컬 세이브 확장" 필드는 로컬 파일(JsonUtility) 전용이며
/// 뒤끝 ToParam 매핑에는 포함되지 않는다(서버 세이브 경로 무변경).
/// </summary>
[Serializable]
public class RunSaveData
{
    public int    slotIndex;          // 0, 1, 2 — 저장 슬롯 번호
    public bool   hasActiveRun;
    public int    chapter;           // ChapterId enum 값
    public int    currentHp;
    public int    maxHp;
    public int    runGold;
    public int    retryCount;        // 이 슬롯에서 새 런을 시작한 누적 횟수
    public int    playSeconds;       // 이 슬롯의 누적 플레이 시간(초) — 이어하기로 계속 쌓인다
    public int    progressPercent;   // 0-100 — 챕터 기반 진행도
    public string characterKey;      // PlayerLoadout.CharacterPrefabKey
    public string characterName;     // CharacterData.characterName (표시용)
    public string weapon0PrefabKey;  // WeaponData.weaponPrefabKey (슬롯 0)
    public string weapon1PrefabKey;  // WeaponData.weaponPrefabKey (슬롯 1)
    public int    itemCount;          // 현재 보유 아이템 수 (빠른 표시용)
    public int    synergyCount;       // 현재 활성 시너지 단계 수 (표시용 — MerlinRuneBridge.ActiveSynergyCount)
    public int    roomClearCount;     // 이 런에서 클리어한 방 수
    public string itemsJson;         // ItemListWrapper JSON
    public string roomLogsJson;           // RoomClearLogWrapper JSON
    public string savedAt;                // ISO8601 UTC
    public bool   isInStartRoom;          // true = 스타트룸 미퇴장 상태 (이어하기 시 StartRoom 재진입)
    public int    currentZoneIndex;       // ZoneProgressionService.CurrentZoneIndex (zone-layout 모드 이어하기)
    public string clearedZoneIndicesJson; // IntListWrapper JSON — 클리어된 존 인덱스 목록

    // ── 로컬 세이브 확장 (PR1: 하데스식 절차생성 이어하기) ──
    public int    saveVersion;            // 마이그레이션용. 현재 1.
    public int    runEssence;             // RunDelta.GainedEssence 중간 적립
    public int    fuelEnhanceMaterial;    // RunFuelBank 강화재료 잔량(이벤트방 연료)
    public int    fuelRuneOre;            // RunFuelBank 원석 잔량(이벤트방 연료)
    public int    potionCount;            // 퀵슬롯 포션 개수
    public int    potionCapacity;         // 퀵슬롯 포션 용량
    public int    weaponCurrentSlot = -1; // 현재 무기 슬롯 인덱스
    public int    weapon0EnhanceLevel;    // WeaponData.enhanceLevel (슬롯 0)
    public int    weapon1EnhanceLevel;    // WeaponData.enhanceLevel (슬롯 1)
    public int    weapon0EvolutionStage;  // WeaponData.evolutionStage (슬롯 0) — 강화 상한 확장분
    public int    weapon1EvolutionStage;  // WeaponData.evolutionStage (슬롯 1)
    public string weapon0LegendId;        // WeaponData.legendId (슬롯 0 승급 분기)
    public string weapon1LegendId;        // WeaponData.legendId (슬롯 1 승급 분기)

    public string relicKey;               // PlayerLoadout.Relic SO 이름(Addressables 키)
    public string covenantsJson;          // CovenantListWrapper JSON
    public string runeCellsJson;          // Vector2IntListWrapper JSON — 룬 보드 점유 셀(시너지 권위)
    public string runePlacementsJson;     // RunePlacementListWrapper JSON — Shape 재구성(재편집)용
    public string stagingItemsJson;       // ItemListWrapper JSON — 보관함 아이템

    // 절차생성 진행 상태 (RunFlowController/RunSequencer)
    public int    masterSeed;
    public int    visitCount;
    public int    seqPhase;
    public int    shopUsed;
    public int    eventUsed;
    public int    crucibleUsed;         // 재련소 방문(캡 소모) — 없으면 이어하기마다 캡이 리셋된다
    public int    refineryUsed;         // 정제소 방문(캡 소모)
    public int    shopMiss;             // PRD 미출현 누적 — 특수방 등장 기대치. 복원 안 하면 초기화된다
    public int    eventMiss;
    public int    crucibleMiss;
    public int    refineryMiss;
    public int    heading;
    public int    anchorToggle;
    public string currentRoomPoolKey;
    public int    currentRoomKind;
    public int    currentRoomMirror;
    public bool   currentRoomCleared;     // 클리어 후 저장 지원 — 복원 시 몹 재스폰 방지 + 출구 개방
    public int    crucibleRollIndex;      // 재련소 결정적 롤 소비 수 — 복원 시 스트림 진행(save-scum 방지)
    public string cooldownsJson;          // CooldownListWrapper JSON
}

// ── 로컬 세이브 확장용 래퍼/엔트리 ──

[Serializable]
public sealed class Vector2IntListWrapper
{
    public List<Vector2Int> items = new();
}

[Serializable]
public sealed class CooldownListWrapper
{
    public List<CooldownKV> items = new();
}

[Serializable]
public sealed class CooldownKV
{
    public string key;
    public int    turns;
}

[Serializable]
public sealed class CovenantListWrapper
{
    public List<CovenantSaveEntry> items = new();
}

[Serializable]
public sealed class CovenantSaveEntry
{
    public string id;
    public int    stage;   // CovenantStage (int)
}

// 룬 보드 배치 — 점유 셀(runeCellsJson)은 시너지 권위, 아래는 Shape 재구성(재편집)용.
[Serializable]
public sealed class RunePlacementListWrapper
{
    public List<RunePlacementEntry> items = new();
}

[Serializable]
public sealed class RunePlacementEntry
{
    public string          instanceId;  // RuntimeItemData.instanceId (인벤토리 재바인딩)
    public int             shapeId;     // shape_id (Shape 재생성)
    public List<Vector2Int> cells = new(); // 점유 셀(col,row)
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

