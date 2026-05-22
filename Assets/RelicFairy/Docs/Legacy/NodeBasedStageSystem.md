# 레거시: 노드 기반 스테이지 시스템

> 이 문서는 2026-05 기준 ZoneLayoutManager/ZoneProgressionService로 대체된
> 구 노드 기반 맵 생성 시스템의 구조를 보존하기 위해 작성되었습니다.
> 실제 코드는 그대로 유지되나 GameRunSession에서 초기화 로직은 제거되었습니다.

---

## 개요

구 시스템은 스테이지 맵을 **노드 그래프**로 표현하고, 플레이어가 노드를 선택해 방으로 진입하는 구조였다.
각 노드는 카테고리(Battle/Elite/Boss/Event/Shop)를 가지며, `RoomManager`가 해당 카테고리의 방을 풀에서 랜덤 선택한다.

---

## 핵심 클래스

### RoomManager
- 파일: `Assets/Abyss/Systems/Stage/Stage/RoomManager.cs`
- Addressables 키: `STAGEDATA_ROOMS` (JSON 배열)
- 역할: 방 데이터 풀을 로드하고 `Pick(category, minDifficulty, maxDifficulty, requiredTags)` 으로 가중치 랜덤 선택
- 주요 데이터: `RoomData { roomId, name, category, difficulty, tags, prefab }`

```csharp
public sealed class RoomManager
{
    public bool IsInitialized { get; private set; }

    public async UniTask InitializeAsync(string addressableKey, Func<string, UniTask<TextAsset>> loader);

    // 카테고리 + 난이도 범위 + 태그로 방 선택
    public RoomData Pick(RoomCategory category, int minDifficulty, int maxDifficulty, string[] requiredTags = null);

    public RoomData GetById(string roomId);
}
```

### StagePointManager
- 파일: `Assets/Abyss/Systems/Stage/Stage/StagePointManager.cs`
- 역할: 챕터별 노드 그래프 생성, 플레이어 이동 관리, 방 카테고리 결정
- 주요 메서드:

```csharp
public sealed class StagePointManager
{
    public int CurrentPointId { get; private set; } = -1;
    public IReadOnlyList<StagePointContext> Contexts { get; }

    // 챕터 기반 노드 그래프 초기화 (새 런 시작 시)
    public void Initialize(ChapterId chapter, RoomManager roomManager);

    // 저장된 그래프로 상태 완전 복원 (이어하기 시)
    public void RestoreFromSaved(SavedStageGraph savedGraph, int currentPointId);

    // 노드 → RoomManager를 통해 방 데이터 확정
    public void Resolve(StagePointContext ctx);
    public void ResolveAll();

    // 시작 노드를 현재 포인트로 설정
    public void SetStartAsCurrent();

    // 이동 가능 여부 + 실제 이동
    public bool CanMove(int targetPointId);
    public bool TryMoveTo(int targetPointId);

    // 방 클리어 마킹
    public void MarkCleared(int pointId);

    // pointId로 컨텍스트 조회
    public StagePointContext GetContext(int pointId);
}
```

### StagePointContext
- 노드 하나의 상태를 담는 데이터 클래스
- `PointId`, `LayerIndex`, `IndexInLayer`, `StageCategory`, `NormalRoomCategory`
- `ResolvedRoomId` — RoomManager.Pick 이후 확정된 방 ID
- `IsCleared`, `IsCurrent`, `IsAccessible`

### StageMapGraph / StageMapNode
- 파일: `Assets/Abyss/Systems/Stage/MapGen/` 또는 `StageMapBootstrapper.cs` 내 정의
- 챕터 맵 전체를 그래프로 표현
- `StageMapNode { PointId, Stage (StageCategory), Normal (NormalRoomCategory), LayerIndex, IndexInLayer, NextPointIds }`
- `StageMapGraph { FullPattern, MiddlePattern, Nodes }`
- `CachedStageGraph` — GameRunSession이 보유, StageMap 씬 재진입 시 Generator 재실행 없이 UI 복원용

### RoomCategory / RoomCategoryUtil
- `RoomCategory { Battle, Elite, Boss, Event, Shop, Start }`
- `RoomCategoryUtil.Parse(string)` — 서버 문자열 → enum

---

## 저장/이어하기 구조

```csharp
// RunSaveData의 레거시 필드
public int    currentPointId;  // StagePointManager.CurrentPointId
public string graphJson;       // SavedStageGraph 직렬화 (JsonUtility)

[Serializable]
public class SavedStageGraph
{
    public string   fullPattern;
    public string   middlePattern;
    public SavedNode[] nodes;
}

[Serializable]
public class SavedNode
{
    public int   pointId;
    public int   stageCategory;
    public int   normalRoomCategory;
    public int   layerIndex;
    public int   indexInLayer;
    public int[] nextPointIds;
}
```

### 복원 흐름 (구 시스템)
1. `RestoreFromSaveAsync` → `LoadStageDataAsync(STAGEDATA_STAGE)` → `RoomManager.InitializeAsync(STAGEDATA_ROOMS)`
2. `StagePointManager.Initialize(chapter, roomManager)`
3. `StagePointManager.RestoreFromSaved(savedGraph, currentPointId)`
4. `CachedStageGraph = GraphFromSaved(savedGraph)` — UI 복원용

---

## 신 시스템과의 비교

| 항목 | 구 시스템 (노드) | 신 시스템 (ZoneLayout) |
|------|-----------------|----------------------|
| 방 데이터 출처 | Addressables `STAGEDATA_ROOMS` | 서버 CDN `CHAPTER#_ZONE_LAYOUT` |
| 맵 구조 | 선형 노드 그래프 | 존(Zone) 목록 + grid_csv |
| 진행 단위 | StagePointContext | ZoneProgressionService |
| 이어하기 | SavedStageGraph + currentPointId | currentZoneIndex + clearedZoneIndices |
| UI | StageMap 씬 (StageMapBootstrapper) | 인게임 출구 게이트 |

---

## 제거된 GameRunSession 코드 요약

다음 코드는 2026-05 정리 시 제거됨. 필요 시 git 이력에서 복원 가능.

```csharp
// 제거된 상수
private const string ROOMS_KEY = "STAGEDATA_ROOMS";
private const string STAGE_KEY = "STAGEDATA_STAGE";

// 제거된 필드
private Dictionary<int, StageData> _stageDataCache;

// 제거된 init (StartNewRunAsync / RestoreFromSaveAsync)
await LoadStageDataAsync(STAGE_KEY, loader);
RoomManager = new RoomManager();
await RoomManager.InitializeAsync(ROOMS_KEY, loader);
StagePointManager = new StagePointManager();
StagePointManager.Initialize(chapter, RoomManager);

// 제거된 메서드
private static StageMapGraph GraphFromSaved(SavedStageGraph saved) { ... }
private async UniTask LoadStageDataAsync(string key, Func<...> loader) { ... }
```

*참고 파일*: `Assets/Abyss/Systems/Stage/Stage/RoomManager.cs`, `StagePointManager.cs`
