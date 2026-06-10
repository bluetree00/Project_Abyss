# Lee — 그리드 블록 시너지 시스템 작업 지침

> 담당자: Lee
> 브랜치: `dev/Lee-Grid` (`develop` 기준으로 생성)
> Last Updated: 2026-03-02

---

## 1. 시스템 개요

**그리드 블록 시너지 시스템**

- 플레이어가 블록(도형)을 그리드에 배치
- 특정 패턴/영역이 채워지면 **시너지** 발동 → 추가 효과 부여
- 퍼즐 클리어 조건이 아닌, **전투 보조 / 강화 메커니즘**으로 동작
- 시너지 효과 연동은 추후 작업 예정, 현재는 그리드/블록 구조에 집중

---

## 2. 현재 진행 상태

- [x] 그리드 기본 구조 구현 (`leeGrid.cs`, `leeGridSquare.cs`)
- [x] 블록(도형) 기본 구현 (`leeShape.cs`, `leeGridManager.cs`)
- [🔄] **SO 데이터 기반 리팩토링** ← **현재 작업**
  - 그리드 형태(배치 가능 셀 패턴) → ScriptableObject
  - 블록(도형) 데이터 → ScriptableObject
- [ ] 시너지 감지 로직 구현
- [ ] 시너지 효과 연동 (추후 KBG와 연동)

---

## 3. 현재 작업: SO 리팩토링

### 3.1 현재 구조 (하드코딩)

현재 그리드 형태와 블록은 코드에 직접 정의되어 있음:

```
leeGrid.cs       — 8x8 셀 배치 패턴 int[] 하드코딩
leeShape.cs      — cellOffsets 리스트로 도형 형태 직접 정의
leeGridManager.cs — 배치/스냅 로직
leeGridSquare.cs  — 셀 상태 관리
```

### 3.2 목표 구조 (SO 기반)

```
GridLayoutSO     — 그리드 형태 (배치 가능 셀 패턴, 보드 크기)
ShapeDataSO      — 블록 도형 데이터 (셀 오프셋, 스프라이트, 이름)
```

**GridLayoutSO 방향:**
```csharp
[CreateAssetMenu(menuName = "Abyss/Grid/GridLayout")]
public class GridLayoutSO : ScriptableObject
{
    public int width;
    public int height;
    public bool[] cellPattern;  // width*height 크기, true = 배치 가능
}
```

**ShapeDataSO 방향:**
```csharp
[CreateAssetMenu(menuName = "Abyss/Grid/ShapeData")]
public class ShapeDataSO : ScriptableObject
{
    public string shapeName;
    public Vector2Int[] cellOffsets;  // 블록 구성 셀 상대 좌표
    public Sprite sprite;             // (선택) 시각적 표현
}
```

### 3.3 리팩토링 포인트

| 대상 | 변경 전 | 변경 후 |
|------|---------|---------|
| 그리드 셀 패턴 | `leeGrid.cs` int[] 하드코딩 | `GridLayoutSO` 인스펙터 설정 |
| 도형 형태 | `leeShape.cs` cellOffsets 직접 정의 | `ShapeDataSO` 어셋 주입 |
| 초기화 | 코드에서 직접 배열 참조 | SO 레퍼런스 → Inspector 드래그 연결 |

---

## 4. 다음 작업: 시너지 감지

> SO 리팩토링 완료 후 진행

### 4.1 시너지 개념

- 그리드의 특정 **영역/패턴**이 블록으로 채워지면 시너지 발동
- 시너지 정의도 SO로 관리 예정

```
SynergyDataSO
├─ 감지할 패턴 (채워져야 할 셀 집합)
├─ 시너지 이름/설명
└─ 효과 ID (추후 KBG 효과 시스템과 연동)
```

### 4.2 감지 로직 방향

```csharp
// 블록 배치 후 호출
public bool CheckSynergy(GridLayoutData board, SynergyDataSO synergy)
{
    // 시너지 패턴의 모든 셀이 채워져 있으면 true
}
```

### 4.3 효과 연동 (추후 KBG 협업)

```
시너지 감지
    ↓
event Action<SynergyDataSO> OnSynergyTriggered
    ↓
(KBG 측) 효과 시스템에서 구독 → 추가 효과 부여
```

> 직접 호출 금지 — **이벤트 방식**으로 연동할 것.

---

## 5. 폴더 구조 (현재 + 목표)

```
Assets/Abyss/
└─ UI/
   └─ leeTestPuzzle/
      └─ Scripts/
         ├─ leeGrid.cs           ← 그리드 생성 (SO 주입 리팩토링 대상)
         ├─ leeGridSquare.cs     ← 셀 상태
         ├─ leeShape.cs          ← 블록 도형 (SO 주입 리팩토링 대상)
         └─ leeGridManager.cs    ← 배치/스냅 로직

Assets/Abyss/
└─ ScriptableObjects/          ← 새로 생성할 SO 어셋 위치
   └─ Grid/
      ├─ GridLayoutSO.cs       ← (생성 필요) 그리드 형태 SO 클래스
      ├─ ShapeDataSO.cs        ← (생성 필요) 블록 도형 SO 클래스
      └─ SynergyDataSO.cs      ← (나중에) 시너지 정의 SO 클래스
```

---

## 6. 아키텍처 규칙 (필독)

- **비동기**: `UniTask` 사용 (코루틴 X)
- **이벤트**: `event Action` 사용 (UnityEvent 지양)
- **데이터**: ScriptableObject로 그리드/블록 설정값 관리, 런타임 상태는 SO에 저장 금지
- **UI 연동**: 그리드 로직이 직접 UI를 건드리지 않음 → 이벤트로 알림
- **시너지 연동**: 직접 호출 금지, `event Action<SynergyDataSO>` 방식으로 KBG 측에 위임

---

## 7. 브랜치 작업 방법

```bash
# 1. develop 최신 받기
git checkout develop
git pull origin develop

# 2. 작업 브랜치 생성
git checkout -b dev/Lee-Grid

# 3. 작업 후 커밋
git add -A
git commit -m "refactor: 그리드/블록 SO 데이터 기반 리팩토링"

# 4. 원격 푸시
git push origin dev/Lee-Grid

# 5. PR 요청 → develop 머지
```

---

## 8. 참고 문서

- [DevTracker.md](DevTracker.md) — 전체 로드맵 및 진행 현황
- [Abyss Core Architecture.md](Abyss%20Core%20Architecture.md) — 전체 아키텍처 (섹션 8: Grid System)
- [BG_Abyss_Worklog.md](BG_Abyss_Worklog.md) — KBG 설계 메모
