# Abyss - Worklog (WIP)

> 이 문서는 개발 중 떠오른 아이디어/결정/이슈/해야할 일들을 누적 기록하는 작업 노트다.  
> **아키텍처 확정 문서(Architecture Spec)** 와 분리해서 운영한다.

---

## 0. Quick Notes (초기 메모)

- ID 조회 → `101 = Bat`
- Type → ID 변환 조회: `Bat01 -> 101 -> 조회`
- 서버 → JSON → class
- 서버 → class (캐싱 X)
- 데이터 테이블이 아닌 **차트(Chart) 기반 접근**
  - 차트 내부에 CSV 업로드
  - 차트 내 ID로 문서 접근 → 서버 연동

### 버전/동기화
- 스탯 업데이트는 **버전**으로 관리하는 게 맞아 보임
- 버전 탐색으로 동기 적용 필요
- JSON은 자동 생성이라 미리 만들 필요 없음

### 현재 애매한 부분
- ID 조회 방식이 모호함
- 타입을 ID에 매칭 → ID 기반으로 서버에서 받아오는 작업 필요
- Define에 ID 매핑해도 됨 (서순 중요)
- 처음 타입 정의와 연결해 ID를 매칭하면 될 듯

### 진행/이슈
- 차트 접근 성공
- 스텟 매칭에서 오류 발생 → 적절한 캐스팅 필요 (타입 디버깅 필요)
- 스텟 매칭되면 JSON화 → 저장/캐싱 후 사용
- 스텟 매칭 완료
- 부모 ID 탐색 → 오버라이드 자동화 구조

---

## 1. 몬스터 AI 상태 설계 메모 (추적/공격)

### 문제
- 공격 상태에서 공격 상태 전환이 어려움
- 추적 ↔ 공격 전환이 너무 빨라져 상태가 이상해짐 (쿨타임 없을 때)

### 해결 방향
- `추적 → (거리 조건) → 공격대기 → (쿨/회전/조건) → 공격 → (공격 후) 추적`
- 공격대기 상태: “공격할 거야” 시즈모드
  - 공격 가능하면 공격
  - 불가능하면 공격대기 유지
  - 대상이 벗어나면 다시 추적

### 정리
- **Trace → AttackReady → Attack → Trace**
- Attack 시점에 쿨타임 적용
- AttackReady에서는 회전 정렬 + 쿨타임/거리 체크

---

## 2. 데미지/충돌/이펙트 처리 메모

### 질문
- 충돌 체크 데미지를 어떻게 넣을 것인가?
- 어떻게 받을 것인가?

### 핵심 인사이트
- 공격마다 타수/간격이 존재 → “받는 쪽”이 제한하기 어려움  
  → “공격자(콜라이더)”가 공격 정보를 갖고 있어야 함

### 이펙트/콜라이더 분리
- 이펙트는 **단순 이미지/표현**
- 실제 판정은 **콜라이더**
- 스킬별 로직/데이터(이펙트 이름 등)를 데이터로 보관

### 공격 간격 처리
- 딕셔너리로 `attackId` 기반 간격을 체크
- 콜라이더 생성 시 해당 공격 데이터가 콜라이더에 주입되어야 함

### 역경직(히트스톱/연출)
- 카메라 흔들림 + 시간 조절(슬로우) + 애니 속도 조절
- 타격 시점에 아주 짧게 슬로우
- 애니 속도를 살짝 낮춰 공격 속도 손실/타격감 확보

### 다단 히트
- 코루틴 기반 처리
- 공격 데이터에 `hitCount`를 포함해서 실행
- 콜라이더 생성 시 데이터 주입 → 피격자가 콜라이더 정보 읽고 연출 실행

---

## 3. 플레이어 구조 개선 메모

### 목표
- 몬스터처럼 **상태 캐싱**
- 공격 데이터를 서버에서 받아 콜라이더/이펙트 생성
- 피격 판정도 콜라이더 정보 기반

### 입력 버퍼(핵심 규칙)
> 입력은 들어올 때 큐에 쌓고, 각 상태 Update에서 허용 구간인지 확인 후 Consume하여 실행한다.

- 입력 시점: Push만
- 상태 Update 시점: TryConsume → 전이/실행
- 버퍼는 만료 정리(갱신)만 계속 수행
- 큐 크기 제한 + FIFO

---


## 4. 새로운 플레이어 방향 (몬스터 FSM 이식)

- 몬스터 FSM 시스템을 플레이어에 적용
- 몬스터 어빌리티를 플레이어 어빌리티로 대체
- 상태에서 컨트롤러의 어빌리티 접근
- 필요한 입력을 특정 상태만 반응

### 장비 연동
- 장착 장비가 있으면 해당 장비 기능에 접근 (if)
- 애니메이션 오버라이드도 이 방식으로 변경
- 오버라이드 담당 서비스 클래스로 분리

### Type → ID 방식(예시)
```csharp
public override Define.MonsterType Type => Define.MonsterType.Bat;
protected override int MonsterId => Define.GetMonsterId(Type);

5. 입력 정책 분리 메모

버퍼 입력이 잘 안 되는 현상 해결 필요

입력을 정책으로 분리하는 방식이 좋아 보임

입력을 인터페이스로 분리

약공/강공 등 입력 방식을 분리

switch-case로 위임

진행

입력 적용 완료

차지 공격이 바로 나감 → 입력 조율 필요

FSM 수정 필요: 공중 공격/대시 공격 등 레이어별 적용 방식

6. Layered FSM 정리 메모

Layered FSM = Locomotion + Action

Loco: 숨쉬듯 상시(Idle/Move)

Action: 공격/스킬 같은 특수 동작

콤보

하나의 상태에서 콤보 SO 데이터 테이블로 접근

공격 상태 전환 시 버퍼/오버라이드/어빌리티 세팅 필요

7. 점프/공중 상태 메모

점프는 블렌드 트리로 구성

시작/중간/끝(착지)로 분리

최고점에서 낙하 상태 전환

Ground 체크로 낙하 시작 판단

공중 공격 시 중력 고정 + 공격 수행 → 종료 시 정상 복귀

콤보 관리

nowCount, endCount

일정 시간이 지나면 콤보 리셋

Air와 Ground 콤보 분리 가능성

8. 공격 애니 슬롯 + 선택적 Ability 레퍼런스

애니 슬롯은:

애니 전용 정보(이펙트/사운드/타이밍)

선택적 Ability 레퍼런스(null 허용)

런타임: 애니 이벤트/AbilityRunner에서 “있으면 실행, 없으면 무시”

9. 장비/서버/Addressables 패키지 방향 (결론 후보)
무기 데이터에 포함될 요소(결론)

스탯

애니메이션 Set

어빌리티 Set

이벤트용 Effect/Collider Set

외부 표기 오브젝트(프리팹/이미지)

(플레이어 손 장착 프리팹)

애니 이벤트 인덱싱

AttackEvent 정수 인자로 stepIndex 전달

attackIndex / eventIndex에 대응하는 2중 배열 or 딕셔너리 필요

예: [attackIndex][eventIndex]

10. 핸들러 기반 설계 메모 (IWeaponUser + Handlers)

플레이어가 모든 무기 로직을 대비하는 건 무리

공통 틀은 인터페이스 + 핸들러로 확장

비유

IWeaponUser = 재료 (기본 도구/자원)

Handler = 조리법(로직)

제안 구조

PlayerWeaponHandler
├─ IEffectHandler
├─ IColliderHandler
└─ IPhysicsHandler

기본 핸들러 + 특별 장비 시 교체 전략


11. Stage 개발 메모
목표

노드를 미리 깔고, 각 노드가 자신을 초기화

랜덤값은 테이블에서 가져옴

GameRunManager가 Run 수명 관리(초기화/종료)

Run에서 씬 UI 수집

씬의 StagePoint UI를 수집해 StagePointManager에 등록

StagePointManager가 상태 저장/그래프 관리

RoomResolver가 카테고리 기반 룸 결정

RoomRepository가 JSON/SO 데이터 제공

구조 요약

[Scene]
 └─ StagePointUI (MonoBehaviour)
      └─ pointId / category / nextPointIds

GameRunManager (Run 수명)
- 씬에서 StagePointUI 수집
- 하위 매니저 조립
- Run 시작 / 종료
- 외부 API 제공

StagePointManager (상태 저장소)
- StagePointContext 관리
- resolvedRoom 보관
- 그래프 구조 관리
- Resolve 호출 트리거

RoomResolver (결정자)
- 카테고리 → 방 선택
- JSON weight / difficulty
- 랜덤 규칙 소유

RoomRepository (데이터 접근)
- JSON / ScriptableObject
- RoomData 제공


다음 작업

스테이지를 실제 생성해주는 StageManager 작업 필요

클릭 시 연결 가능하면 해당 ID의 방을 Addressables에서 생성

이동 후 다음 ID 연결 검사

12. Addressables + Pool 연결 메모

AddressableManager: 로드 담당

Pooler: 로드된 리소스 사용

Spawn 요청 시 “없으면 로드 → 풀 초기화 → 스폰”이 자연스럽게 이어져야 함

프리로드 기능은 AddressableManager가 지원 가능

13. UI 제작 세팅 메모
Canvas 권장 세팅

Render Mode: Screen Space - Overlay

Canvas Scaler: Scale With Screen Size

Reference Resolution: 1920x1080

Match: 0.5

Graphic Raycaster: 입력 받는 캔버스만 켬

프로젝트 전체 기준 해상도/Match는 통일

UI 아키텍처 규칙

WHEN = Presenter

WHAT = Provider

HOW = View

이벤트(델리게이트) 기반 UI 통제

작업 로그

2026-02-19 16:28: HUD 유지/연동 기본, 폰트 작업 완료

2026-02-24 14:23: HUD 연동 작업 필요, Run bool 이벤트 기반 활성화

데이터 연동은 직렬화 기반(깊은 복사) + 데이터 생길 때까지 await 대기 후 바인딩

14. GameFlow / RunFlow / Overlay Spec (추가)

GameFlow

[Boot] → (App Start) → [Splash]
[Splash] → (SplashFinished) → [Lobby]
[Lobby] → (StartGame + SelectionComplete) → [RunLoad]
       → (OpenCodex) → [Lobby] // Overlay Push
       → (OpenSettings) → [Lobby]
       → (ExitGame) → [Exit]
[RunLoad] → (RunSceneLoaded + InitComplete) → [Run]
[Run] → (RunClear) → [Result]
     → (RunFail) → [Result]
[Result] → (BackToLobby) → [Lobby]
        → (RestartRun) → [RunLoad]
[Exit] → (ConfirmExit) → ApplicationQuit


RunFlow

[RunMap] → (SelectCombatNode) → [RunCombatLoad]
        → (SelectPuzzleNode) → [RunPuzzleLoad]
        → (SelectBossNode) → [RunBossLoad]

[RunCombatLoad] → (LoadComplete) → [RunCombat]
[RunCombat] → (CombatWin) → [RunTravel]
          → (PlayerDead) → [RunEnd]
[RunTravel] → (TravelFinished) → [RunMap]

[RunPuzzleLoad] → (LoadComplete) → [RunPuzzle]
[RunPuzzle] → (PuzzleSuccess) → [RunTravel]
          → (PuzzleFail) → [RunTravel or RunEnd]

[RunBossLoad] → (LoadComplete) → [RunBoss]
[RunBoss] → (BossDead && IsFinalBoss) → [RunClear]
        → (BossDead && !IsFinalBoss) → [RunChapterClear]
        → (PlayerDead) → [RunEnd]

[RunChapterClear] → (NextChapterExists) → [RunNextChapter]
[RunNextChapter] → (InitComplete) → [RunMap]
[RunClear] → (NotifyGameFlow) → GameFlow.RunClear
[RunEnd] → (NotifyGameFlow) → GameFlow.RunFail


Overlay ESC Rule

If (OverlayStack.Count > 0)
    ESC → Pop Top Overlay
Else if (CurrentGameState == Run)
    ESC → Push PauseMenu


Overlay Policy

PauseMenu: PauseGame = true, BlockInput = true
Inventory: PauseGame = true(or Slow), BlockInput = true
Settings : PauseGame = depends on context, BlockInput = true
Codex    : PauseGame = depends on design, BlockInput = true


Enums

enum GameState { Boot, Splash, Lobby, RunLoad, Run, Result, Exit }

enum RunState
{
    Map, CombatLoad, Combat, Travel,
    PuzzleLoad, Puzzle,
    BossLoad, Boss,
    ChapterClear, NextChapter,
    Clear, End
}


