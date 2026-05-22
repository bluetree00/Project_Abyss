# Abyss - Core Architecture Documentation

> Project Abyss
> Roguelike Action PC Game
> Last Updated: 2026-05-11

---

# 1. Core Design Principles

## 1.1 설계 원칙 (Principles)

- **Loose Coupling (느슨한 결합)**
- **Dependency Inversion Principle (DIP)**
- **Decoupling**
- **Event-Driven Architecture**
- **Separation of Concerns (SoC)**
- **Data-Driven Design**
- **Runtime State Isolation**

---

# 2. Primary Design Patterns

- Template Method Pattern
- Service Locator Pattern
- Facade Pattern
- Singleton Pattern
- State Pattern + Layered FSM
- Strategy Pattern
- Observer Pattern
- Object Pool Pattern
- Adapter / Bridge Pattern (Animation Event Bridge)

---

# 3. Managers Core Architecture

## 3.1 접근 구조

- `Managers.Instance` (Singleton)
- `Managers.UI` (Service Locator 접근)
- 내부 매니저를 하나의 진입점으로 감싸는 Facade 구조

---

## 3.2 Core Managers (게임 코어)

| Manager | Responsibility |
|----------|---------------|
| InputManager | 입력 업데이트 / 키 체크 (버퍼 제거 예정) |
| ~~ResourceManager~~ | ~~기존 리소스 관리~~ — **제거됨** (2026-05-11, Addressables 전환 완료) |
| AddressableManager | Addressables 로드 / 초기화 |
| ObjectPoolerManager | 오브젝트 풀링 서비스 |
| AnimationResourceManager | 애니메이션 클립 프리로드 / 조회 |

---

## 3.3 Gameplay / UI / Data

| Manager | Responsibility |
|----------|---------------|
| UIManager | UI 팝업 / 노출 관리 |
| CharacterDataManager | 캐릭터 데이터 등록 |
| PlayerManager | 플레이어 관리 |
| MonsterDataManager | 몬스터 데이터 관리 |

---

## 3.4 게임 흐름 시스템 (Game Flow & Run)

> `GameFlowManager`, `GameRunManager` 제거됨 (2026-03). 아래 구조로 교체.

| 클래스 | 역할 |
|--------|------|
| `GameFlow` | 앱 씬 전환 상태 기계 (Title → Lobby → InGame → Result), 순수 C# |
| `GameRunSession` | 단일 런 전체 상태 관리, 순수 C# — Managers 의존 없음 |

---

## 3.5 레이어 아키텍처 (3-Layer)

Managers / Bootstrappers / Flow-Session 3계층으로 책임 분리.

```
Managers       인프라 레이어 (DDOL)
    │           Input / Addressable / ObjectPool / UI / Animation
    │           → 기술 서비스만, 게임 로직 모름
    ↓ (접근)
Bootstrappers  배선 레이어 (씬 진입 시 실행)
    │           AppBootstrapper / GameRunBootstrapper
    │           HudBootstrapper / UIRootBootstrapper
    │           → Managers로 로드 → Flow/Session에 주입
    ↓ (생성·주입)
Flow/Session   게임 로직 레이어 (순수 C#)
                GameFlow / GameRunSession
                → Managers 모름, 이벤트 & 상태만 관리
```

**의존 규칙**

| 레이어 | 아는 것 | 모르는 것 |
|--------|---------|-----------|
| Managers | Unity, 에셋, 입력 | 게임 규칙, 런 상태 |
| Bootstrappers | Managers + Flow/Session | 게임 규칙 세부사항 |
| Flow/Session | 게임 상태, 이벤트 | Managers, Unity 씬 구조 |

**Addressables 로드 의존도 제거 (2026-03)**

`GameRunSession`과 `RoomManager`가 `Managers.AddressableManager`를 직접 참조하던 구조를 제거.
loader 델리게이트를 `GameRunBootstrapper`에서 주입하는 방식으로 교체.

```csharp
// GameRunBootstrapper — Managers 접점은 여기서만
await run.StartNewRunAsync(chapter, LoadTextAsset);

static UniTask<TextAsset> LoadTextAsset(string key) =>
    Managers.AddressableManager.LoadAssetAsync<TextAsset>(key);

// GameRunSession / RoomManager — Managers 모름
public async UniTask StartNewRunAsync(ChapterId chapter, Func<string, UniTask<TextAsset>> loader)
public async UniTask InitializeAsync(string key, Func<string, UniTask<TextAsset>> loader)
```

---

# 4. Player Architecture

플레이어는 3단계 상속 구조로 설계됨.


CharacterBase
↓
PlayerController
↓
Knight (직업 구현)



---

## 4.1 CharacterBase

- Animator / Rigidbody 캐싱
- Transform 접근
- InitAsync
- FreezeRotation
- 공통 Update 흐름

---

## 4.2 PlayerController

### 1) Runtime Data
- CharacterData (SO 템플릿)
- PlayerRuntimeStats (HUD 구독 대상)
- 데미지 / 힐 / 이벤트 브릿지

### 2) Weapon System
- PlayerWeaponManager
- WeaponEffectHandler
- IAttackInputPolicy
- 애니메이션 오버라이드 처리

### 3) Input System
- PlayerInputActions
- InputBuffer
- Command 라우팅

### 4) Layered FSM

#### Locomotion FSM
- Idle / Move / Air / Dodge
- 이동 벡터 적용
- 점프 처리
- 공중 판정
- 회피 처리

#### Action FSM
- Attack
- Q / E Skill
- Charge
- 콤보 단계 제어

---

## 4.3 Knight (직업 구현)

- Loco FSM 상태 등록
- Action FSM 상태 등록
- 입력 라우팅 구현
- 카메라 기준 이동 벡터 계산
- 콤보 타이머 관리

---

## 4.4 Player Layered FSM 개념

플레이어는 2개의 FSM을 병렬 운영함.


Locomotion State
+
Action State
=
최종 플레이어 상태




### 예시

| Locomotion | Action | 실제 상태 |
|------------|--------|------------|
| Move | None | 이동 중 |
| Move | Attack | 이동 공격 |
| Air | HeavyAttack | 공중 강공 |
| Idle | QSkill | 정지 스킬 |

---

# 5. Weapon & Ability Architecture

데이터 기반 + 슬롯 장착 구조 + 이벤트 기반 연동 구조

## 구성 요소

- `IWeaponProvider`
- `WeaponAbilitySetSO`
- `WeaponEffectHandler`
- Animation Event
- ObjectPool

---

## 장비 데이터 흐름
WeaponSO (정적 데이터)
↓
WeaponData (런타임 복사본)
↓
PlayerWeaponManager (슬롯 관리)
↓
OnWeaponChanged 이벤트
↓
PlayerController (입력 정책 / 애니메이션 변경)



---

## 공격 처리 흐름
공격 입력
↓
FSM 전이
↓
애니메이션 실행
↓
Animation Event 발생
↓
WeaponEffectHandler 실행
↓
ObjectPool에서 Effect / Collider 생성


---

# 6. UI Architecture

## UI 유형

- HUD (항상 표시)
- Popup (모달 UI)
- Menu (전체 화면 UI)
- Overlay (연출 / 차단)
- WorldSpace (월드 좌표 UI)

---

## UIRoot 구조
@UIRoot (DontDestroyOnLoad)
│
├─ Canvas_HUD
│ └─ @HUD
│
├─ Canvas_Menu
│ └─ @Menu
│
├─ Canvas_Overlay
│ └─ @Overlay
│
├─ Canvas_Popup
│ └─ @Popup
│
└─ Canvas_WorldSpace
└─ @WorldSpace


각 UI는 자신의 역할에 맞는 Canvas의 자식으로 생성됨.

---

# 7. Stage System (Data-Driven)

## 기획 방향

- 모든 스테이지는 노드 연결 방식
- 비선형 트리 구조
- 플레이어가 연결된 노드로만 이동 가능

---

## 데이터 예시

```json
{
  "roomId": "battle_001",
  "name": "Slime Forest",
  "category": "Battle",
  "difficulty": 1,
  "weight": 10,
  "prefab": "Room_Battle_SlimeForest",
  "tags": ["normal", "early"]
}


방 선택 로직

StagePoint가 "Battle" 요청
    ↓
RoomManager 필터링 (category)
    ↓
difficulty 조건 검사
    ↓
weight 기반 랜덤 선택
    ↓
roomId 결정
    ↓
prefabKey 전달
    ↓
StageMapSpawner.ChangeMap()


전체 Run 흐름

GameRunBootstrapper (씬 진입)
    ↓
GameRunSession.StartNewRunAsync(chapter, loader)
    ├─ RoomManager.InitializeAsync(loader)
    ├─ StagePointManager.Initialize(chapter, roomManager)
    └─ PlayerState 생성
    ↓
RegisterPoints / ResolveAllPointsAndSetStart
    ↓
SpawnCurrentPointMap
    → StagePointManager → RoomManager → StageMapSpawner.ChangeMap()


End of Stage Section

---

# 8. Grid Puzzle System

## 8.1 개요

- 던전 내 퍼즐 방에서 실행되는 블록 기반 퍼즐 시스템
- RunFlow 상의 `RunPuzzle` 상태에서 활성화
- 클리어 시 `PuzzleSuccess` → `RunTravel` 전환

## 8.2 구성 요소 (구현 중 — Lee 담당)

| 클래스 | 역할 |
|--------|------|
| GridBoard | 보드 상태 관리 (2D 배열, 블록 배치) |
| GridBlock | 블록 단위 데이터 (타입, 위치, 상태) |
| BlockType (enum/SO) | 블록 종류 정의 |
| GridPuzzleValidator | 클리어 조건 검사 |
| GridPuzzlePresenter | UI 연동 (View 업데이트) |

## 8.3 GameRunSession 연동 포인트

```
GameRunSession.RequestHudMode(HUDIds.Mode.Puzzle)
    ↓
퍼즐 클리어 판정
    ↓
GameRunSession.EndRun(isCleared: bool)
```

---

# 9. GameFlow & RunFlow 전체 흐름

## 9.1 GameFlow

```
[Boot] → [Splash] → [Lobby]
[Lobby] → (StartGame) → [RunLoad] → [Run]
[Run] → (RunClear / RunFail) → [Result]
[Result] → [Lobby] or [RunLoad]
```

## 9.2 RunFlow

```
[RunMap]
  ├─ SelectCombatNode → [RunCombat] → (Win) → [RunTravel] → [RunMap]
  ├─ SelectPuzzleNode → [RunPuzzle] → (Success/Fail) → [RunTravel] → [RunMap]
  └─ SelectBossNode   → [RunBoss]
       ├─ (BossDead && FinalBoss)   → [RunClear]
       ├─ (BossDead && !FinalBoss)  → [RunChapterClear] → [RunMap]
       └─ (PlayerDead)              → [RunEnd]
```

## 9.3 RunState Enum

```csharp
enum RunState
{
    Map, CombatLoad, Combat, Travel,
    PuzzleLoad, Puzzle,
    BossLoad, Boss,
    ChapterClear, NextChapter,
    Clear, End
}
```

---

# 10. 브랜치 & 개발 규칙

## 10.1 브랜치 전략

```
main          ← 릴리즈 (직접 푸시 금지)
  └─ develop  ← 통합 브랜치
       ├─ dev/KBG-UI     ← KBG: UI 작업
       └─ dev/Lee-Grid   ← Lee: 그리드 블록 로직
```

## 10.2 코드 컨벤션

- 비동기: `UniTask` (코루틴 사용 금지)
- 리소스 로드: `AddressableManager` 경유 (Resources.Load 사용 금지)
- 이벤트: C# `event Action` 기반 (UnityEvent 지양)
- UI 데이터 흐름: `Provider → Presenter → View` 3단 구조 준수
- ScriptableObject: 정적 데이터(설정값)만 사용, 런타임 상태 저장 금지

---

# 정리

| 시스템 | 상태 | 담당 |
|--------|------|------|
| 코어 아키텍처 (3-Layer: Manager/Bootstrapper/Flow) | ✅ 완료 | KBG |
| FSM (플레이어/몬스터) | ✅ 구조 완료 | KBG |
| HUD 자동화 (Presenter/Mode) | ✅ 완료 | KBG |
| Stage/Run 루프 | ✅ 구조 완료 | KBG |
| UI 위젯 연결 | 🔄 진행중 | KBG |
| 그리드 퍼즐 시스템 | 🔄 진행중 | Lee |
| 전투 판정 시스템 | 📋 예정 | KBG |
| 몬스터 AI 신규 구현 | 📋 예정 | KBG |
| 아이템/장비 시스템 | 📋 예정 | 미정 |
| Quest 시스템 | 📋 재설계 예정 | 미정 |