# Lee 작업 내역 - 그리드 퍼즐 시스템 (leeTestPuzzle)

> 경로: `Assets/Abyss/UI/leeTestPuzzle/Scripts/`
> 작성일: 2026-03-04

---

## 개요

드래그&드롭 기반의 그리드 퍼즐 UI 시스템.
플레이어가 셰이프(조각)를 드래그하여 그리드 칸에 배치하고,
배치 가능한 칸을 모두 채우면 이벤트를 발생시키는 구조.

**게임 내 역할:** GridSynergy (그리드 시너지) 퍼즐 화면의 프로토타입/테스트 구현체

---

## 전체 구조

```
LeeBoardManager (진입점/세션 관리)
    ├── leeGrid (그리드 런타임 인스턴스)
    │     └── leeGridSquare (개별 칸)
    ├── leeShape (셰이프 런타임 인스턴스 + 드래그)
    └── leeGridManager (배치 판정 매니저)

ScriptableObject (데이터 정의)
    ├── LeeBoardConfigSO    - 보드 전체 설정
    ├── LeeGridAssetSO      - 그리드 에셋 (패턴 + 비주얼 + 셰이프 목록)
    ├── LeeGridPatternSO    - 그리드 행/열 패턴 (0/1 문자열)
    ├── LeeGridVisualSO     - 그리드 비주얼 설정 (색상, 간격, 스케일)
    ├── LeeShapeAssetSO     - 셰이프 정의 (블록 프리팹 + 셀 오프셋)
    ├── LeeShapeDragSO      - 드래그 시 비주얼 설정
    └── LeePlacementRulesSO - 배치 허용 거리 규칙

유틸리티
    ├── LeeShapeBoundsUtility - 셰이프 셀 범위 계산 (static helper)
    └── LeeGridSelectButton   - 선택 UI 버튼 (그리드 진입 트리거)
```

---

## 스크립트별 상세

### `LeeBoardManager.cs` (핵심 진입점)

**역할:** 전체 보드 흐름 관리. 싱글톤.

**주요 기능:**
- 선택 화면(selectionRoot) ↔ 게임플레이 화면(gameplayRoot) 전환
- 그리드 세션 생성 및 캐싱 (`GridSession` 내부 클래스)
- 셰이프 슬롯 배치 (Y축 스텝 방식, 최대 5슬롯)
- 그리드 채움 완료 이벤트 발행 (`OnGridFilled`, `onGridFilledUnity`)

**흐름:**
```
선택화면: LeeGridSelectButton 클릭
    → EnterGrid(gridAsset)
        → CreateSession() : Grid + Shape 인스턴스 생성 (cacheRoot에 비활성 보관)
        → ActivateSession() : gridHost / shapeHost 에 이동 후 활성화
        → leeGridManager.SetActiveGrid()
    → 플레이어 셰이프 드래그 배치
    → 모든 칸 채움 → NotifyGridFilled() → 이벤트 발행
    → BackToSelection() : 세션 비활성화, 선택화면 복귀
```

**세션 관리 핵심 설계:**
- 그리드/셰이프를 최초 1회만 생성 후 `cacheRoot`에 보관
- 화면 전환 시 `SetParent`로 호스트 이동 (재생성 없음 → 상태 보존)
- 배치된 셰이프와 미배치 셰이프를 구분해 각각 gridHost / shapeHost 로 이동

**주요 필드:**
| 필드 | 설명 |
|------|------|
| `boardConfig` | LeeBoardConfigSO - 전체 설정 |
| `selectionRoot` | 선택 UI 루트 GameObject |
| `gameplayRoot` | 게임플레이 UI 루트 GameObject |
| `gridHost` | 그리드가 붙는 RectTransform (왼쪽) |
| `shapeHost` | 셰이프가 붙는 RectTransform (오른쪽) |
| `gridPrefab` | leeGrid 컴포넌트 보유 프리팹 |
| `shapePrefab` | leeShape 컴포넌트 보유 프리팹 |
| `spawnOrigin` | 첫 슬롯 위치 (shapeHost 기준) |
| `spawnSlotStepY` | 슬롯 간격 Y값 |
| `maxSpawnSlots` | 최대 슬롯 수 (기본 5) |
| `gridEvents` | 특정 그리드 채움 완료 시 호출할 UnityEvent 목록 |

---

### `leeGrid.cs` (그리드 런타임)

**역할:** 그리드 인스턴스. LeeGridAssetSO 데이터로 칸(leeGridSquare)들을 생성.

**주요 메서드:**
| 메서드 | 설명 |
|--------|------|
| `Initialize(asset)` | LeeBoardManager에서 호출. gridAsset 설정 후 Rebuild() |
| `Rebuild()` | 기존 자식 삭제 후 rows×cols 만큼 leeGridSquare 재생성 |
| `GetGridSquares()` | 전체 칸 리스트 반환 |

**칸 배치 로직:**
- `autoCenter = true` : (0,0) 기준 자동 센터링
- `autoCenter = false` : `startPosition`에서 시작
- 칸 간격 : `squareGap`, 스케일 : `squareScale`
- `pattern.IsPlaceable(r, c)` 로 배치 가능 여부 결정

---

### `leeGridSquare.cs` (개별 칸)

**역할:** 그리드의 한 칸. 배치 가능/불가, 점유 상태, 하이라이트 관리.

**상태:**
| 상태 | 설명 |
|------|------|
| `isPlaceable` | 셰이프를 놓을 수 있는 칸 여부 |
| `isOccupied` | 현재 셰이프가 점유 중인지 |
| `isHighlighted` | 드래그 호버 하이라이트 활성 여부 |

**비주얼:**
- `baseImage` : 배경 이미지 (placeableColor / blockedColor)
- `activeImage` : 배치 완료 시 표시 이미지
- `hoverImage` : 드래그 중 hover 하이라이트 이미지

**Physics2D 트리거 방식으로 호버 감지:**
- `OnTriggerEnter2D` / `OnTriggerStay2D` / `OnTriggerExit2D`
- 태그 `"ShapeBlock"` 대상으로만 반응
- overlapCount 카운터로 여러 블록 동시 hover 처리

---

### `leeShape.cs` (셰이프 런타임 + 드래그)

**역할:** 셰이프 인스턴스. 드래그 이벤트 처리 및 배치 관리.

**드래그 흐름:**
```
OnBeginDrag → 기존 점유 해제(ReleaseShape), 스케일 확대, pointerOffset 적용
OnDrag      → anchoredPosition += delta / scaleFactor
OnEndDrag   → 스케일 복원 → TryPlaceShape() 시도
              → 실패 시 ReSlotAndReturn() (슬롯 위치로 복귀)
```

**주요 메서드:**
| 메서드 | 설명 |
|--------|------|
| `ApplyAsset(asset)` | LeeShapeAssetSO 적용 후 블록 재빌드 |
| `BuildShapeBlocks()` | cellOffsets 기반 자식 블록 생성 |
| `CacheStartTransform()` | 현재 위치/스케일 캐싱 (복귀용) |
| `ReturnToStart()` | 캐시된 위치/스케일로 복귀 |
| `SetHome(parent, pos)` | 홈 슬롯 지정 |
| `ReturnHome()` | 홈 슬롯으로 복귀 |
| `SetOccupiedSquares(list)` | 점유 칸 목록 설정 |
| `GetOccupiedSquares()` | 점유 칸 목록 반환 |

---

### `leeGridManager.cs` (배치 판정 매니저)

**역할:** 셰이프 배치 판정의 핵심 로직. 싱글톤.

**`TryPlaceShape(shape)` 알고리즘:**
1. shape의 모든 자식 블록(RectTransform) 순회
2. 각 블록에서 가장 가까운 `leeGridSquare` 탐색 (`FindClosestSquare`)
3. 해당 칸이 `isPlaceable` 이고 `!isOccupied` 인지 확인
4. 모든 블록 통과 시 → 첫 블록과 첫 타겟 칸의 worldDelta로 스냅 이동
5. 모든 칸 `SetOccupied(true)`, 하이라이트 해제
6. `CheckAllPlaceableFilled()` 호출

**`FindClosestSquare(blockRT)` 알고리즘:**
- gridRoot 좌표계 기준 거리 계산 (스케일 독립적)
- 허용 거리 = `squareGap * maxAllowedDistMultiplier`
- 허용 거리 초과 시 null 반환 (배치 실패)

**`ReleaseShape(shape)` :**
- 점유 중인 모든 칸을 `SetOccupied(false)`, 하이라이트 해제
- 재드래그 시 호출

**`CheckAllPlaceableFilled()` :**
- 모든 `isPlaceable` 칸이 `isOccupied` 이면 `LeeBoardManager.NotifyGridFilled()` 호출

---

### `LeeGridSelectButton.cs` (선택 UI 버튼)

**역할:** 선택 화면에서 그리드 선택 버튼. `IPointerClickHandler` 구현.

```csharp
OnPointerClick → LeeBoardManager.Instance.EnterGrid(gridAsset)
```

---

### `LeeShapeBoundsUtility.cs` (유틸리티)

**역할:** LeeShapeAssetSO의 셀 범위를 RectInt로 계산하는 static 헬퍼.

- SpawnAllShapes 시 수동 레이아웃 너비 계산에 사용
- SO 구조 변경 없이 외부에서 bounds 계산 가능

---

## ScriptableObject 데이터 구조

### `LeeBoardConfigSO` - 보드 전체 설정

| 필드 | 기본값 | 설명 |
|------|--------|------|
| `startInSelectionMode` | true | 시작 시 선택화면 표시 여부 |
| `gameplayUniformScale` | 1f | 그리드+셰이프 통합 스케일 |
| `randomShapeOnSelect` | true | 셰이프 랜덤 선택 여부 |
| `spawnAllShapes` | true | 전체 셰이프 스폰 여부 (false면 1개만) |
| `shapeSpacing` | 60f | 수동 레이아웃 셰이프 간격 (px) |

### `LeeGridAssetSO` - 그리드 에셋

| 필드 | 설명 |
|------|------|
| `pattern` | LeeGridPatternSO - 행/열 패턴 |
| `visual` | LeeGridVisualSO - 비주얼 설정 |
| `spawnableShapes` | LeeShapeAssetSO[] - 이 그리드에 할당된 셰이프들 |
| `onAllPlaceableFilled` | UnityEvent - 채움 완료 이벤트 |

### `LeeGridPatternSO` - 그리드 패턴

- `rows`, `columns` : 행/열 수
- `rows01` : 각 행을 `"10110..."` 형식 문자열로 정의 (`'1'` = 배치 가능, `'0'` = 막힘)

### `LeeGridVisualSO` - 비주얼 설정

| 필드 | 설명 |
|------|------|
| `gridSquarePrefab` | 칸 프리팹 (leeGridSquare 포함) |
| `squareGap` | 칸 간격 (기본 90f) |
| `autoCenter` | 그리드 자동 센터링 여부 |
| `squareScale` | 칸 스케일 (기본 0.9f) |
| `placeableColor` | 배치 가능 칸 색상 |
| `blockedColor` | 막힌 칸 색상 |

### `LeeShapeAssetSO` - 셰이프 에셋

| 필드 | 설명 |
|------|------|
| `shapeBlockPrefab` | 블록 프리팹 |
| `cellOffsets` | Vector2Int[] - 셀 좌표 오프셋 배열 |
| `cellSize` | 셀 크기 (기본 90f) |

### `LeeShapeDragSO` - 드래그 설정

| 필드 | 기본값 | 설명 |
|------|--------|------|
| `selectedScale` | (1.1, 1.1, 1) | 드래그 중 확대 스케일 |
| `pointerOffset` | (0, 50) | 드래그 시작 시 포인터 오프셋 |

### `LeePlacementRulesSO` - 배치 규칙

| 필드 | 기본값 | 설명 |
|------|--------|------|
| `maxAllowedDistMultiplier` | 0.5f | 허용 거리 = squareGap × 이 값 |
| `snapShapeToFirstSquare` | true | 첫 블록 기준 스냅 여부 |

---

## 주요 설계 결정 사항

### 1. Scale/Position 분리 문제 해결
선택 UI와 게임플레이 UI를 분리하여 스케일 충돌 방지.
- **선택 화면**: selectionRoot (버튼/이미지)
- **게임플레이 화면**: gameplayRoot (gridHost + shapeHost)
- `gameplayUniformScale` 하나로 그리드+셰이프 스케일 통일

### 2. 세션 캐싱 (재생성 방지)
그리드/셰이프를 최초 1회만 생성 후 cacheRoot에 보관.
화면 전환 시 `SetParent()`로 이동 → 배치 상태 유지됨.

### 3. 거리 계산 좌표계 통일
gridRoot의 InverseTransformPoint 사용 → UI 스케일에 독립적인 거리 계산.

### 4. 슬롯 기반 셰이프 배치
셰이프마다 고정 슬롯 번호 부여 → 화면 전환 후 복귀 시 동일 슬롯 유지.

---

## 파일 위치

```
Assets/Abyss/UI/leeTestPuzzle/Scripts/
├── LeeBoardManager.cs
├── leeGrid.cs
├── leeGridManager.cs
├── leeGridSquare.cs
├── leeShape.cs
├── LeeGridSelectButton.cs
├── LeeShapeBoundsUtility.cs
└── SO/
    ├── LeeBoardConfigSO.cs
    ├── LeeGridAssetSO.cs
    ├── LeeGridPatternSO.cs
    ├── LeeGridVisualSO.cs
    ├── LeePlacementRulesSO.cs
    ├── LeeShapeAssetSO.cs
    └── LeeShapeDragSO.cs
```
