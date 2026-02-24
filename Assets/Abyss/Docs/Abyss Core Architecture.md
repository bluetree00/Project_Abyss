# Abyss - Core Architecture Documentation

> Project Abyss  
> Roguelike Action PC Game  
> Last Updated: 2026-02-24

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
| ResourceManager | 기존 리소스 관리 (Addressables 도입으로 제거 예정) |
| AddressableManager | Addressables 로드 / 초기화 |
| ObjectPoolerManager | 오브젝트 풀링 서비스 |
| AnimationResourceManager | 애니메이션 클립 프리로드 / 조회 |

---

## 3.3 Gameplay / UI / Data

| Manager | Responsibility |
|----------|---------------|
| UIManager | UI 팝업 / 노출 관리 |
| CharacterDataManager | 캐릭터 데이터 등록 |
| GameEventManager | 이벤트 브로커 |
| PlayerManager | 플레이어 관리 |
| MonsterDataManager | 몬스터 데이터 관리 |
| SceneManagerEx | 씬 관리 확장 |

---

## 3.4 Run 단위 시스템

- `GameRunManager`
  - Run 전체 흐름 제어
  - `Managers.GameRun` 으로 접근

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

GameRunManager
    ↓
RoomDataManager
    ↓
RoomSelectManager
    ↓
StagePointManager
    ↓
StageManager


End of Document

---

# 정리