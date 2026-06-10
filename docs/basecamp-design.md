# BaseCamp(베이스캠프) 영속 허브 — MVP 구현 설계

> 상태: **설계 문서 (구현 전)**
> 전제(확정): 현재 챕터1 내부의 **시작방(StartRoom)을 영속 BaseCamp 씬으로 승격**한다. 게임 시작·사망·클리어 시 BaseCamp에서 시작한다(StartRoom 흡수 방향).
> 관련 문서: `docs/procgen-design.md`(하데스형 절차 진행), `docs/hades-save-design.md`(이어하기 세이브)
> 미정 기획 2건은 본 MVP 골격과 분리하여 **"결정 대기"**로 표기: ① 메타 통화 네이밍(영혼 파편) ② 서약 Ascension화.

---

## 0. 핵심 요약 (먼저 읽기)

**한 줄:** 지금의 "시작방"은 독립 오브젝트가 아니라 **챕터 씬 안에서 매 런 절차생성되는 Zone 0**이다. BaseCamp 승격 = 이 Zone 0 구성(선택대·각성제단·게이트)을 **손으로 배치한 영속 씬으로 분리**하고, 던전(절차생성)은 챕터 씬에 그대로 두며, **사망/클리어 후 BaseCamp로 돌아오는 배선을 신설**하는 작업이다.

확정된 정밀 사실 5가지:
1. **StartRoom = 절차생성 Zone 0.** `GameRunBootstrapper.StartRoomAsync()`(`GameRunBootstrapper.cs:1792`)가 `SpawnStartZoneFromLayoutAsync`(`:509`)로 ZoneLayout의 `zone_index==0`을 빌드. 선택대는 CP/WP 타일에서 스폰, 각성제단은 코드 스폰(`SpawnAwakeningAltar` `:1821` → `WorldAwakeningAltar.SpawnAt(spawn + (4,0,2))`), 게이트는 Zone 0 출구에 배치(`CreateStartRoomGates` `:1012`).
2. **던전은 허브에서 2000f 떨어진 앵커에 격리 빌드.** `StartProcGenRunAsync()`(`:705`) → `flow.StartRunAsync(new Vector3(0,0,2000f), poolKey)`(`:719`). 같은 씬 안에서 허브(원점)와 던전이 공존하므로 오프셋이 필요했음.
3. **현재 진입은 Lobby → GameScene_Ch1 직행.** `UI_Lobby.OnClickStartRun`(`UI_Lobby.cs:49`) → `AppBootstrapper.RequestStartRun`(`AppBootstrapper.cs:165`) → `RequestLoad(GameScene_Ch1)`(`:185`). **BaseCamp 미경유.**
4. **사망/클리어 → 복귀는 사실상 미배선.** `GameRunSession.EndRun()`(`GameRunSession.cs:313`)은 존재하나 **호출처 미발견**, `UI_Result`는 빈 스텁(`UI_Result.cs`), `GameOver=140` UIId만 정의(`UIIds.cs:57`). `AppBootstrapper.EndRun()`(`:71`)은 세이브 정리만 하고 씬 이동 없음.
5. **BaseCamp.unity = 빈 스텁, 아무도 로드 안 함.** 씬 내용물=`AwakeningAltar` 1개+카메라+라이트. `RequestLoad(Define.Scene.BaseCamp)` 호출 0건. `BaseCampBootstrapper` 클래스 부재(`AppBootstrapper.cs:569` `case BaseCamp:`는 빈 분기).

권고 골격: **BaseCamp = 손배치 영속 씬** + **GameScene_ChN = 순수 절차 던전(Zone 0 시작방 경로 비활성)** + **사망/클리어 → BaseCamp 복귀 신규 배선**.

---

## 0.5 확정 흐름 (Flow Hardening — 착수 전 확인 3건 결과 반영)

### 확인 3건 결과 (코드로 닫음)

**① 사망/클리어 → EndRun 경로 = 전부 미배선 (숨은 경로 없음).**
- `GameRunSession.EndRun(bool isCleared)`(`GameRunSession.cs:313`)는 **호출처 0건**(`.EndRun(true/false)` 전수 검색 결과 정의·`AppBootstrapper.EndRun()` 무인자만 존재).
- 플레이어 사망: `PlayerController.TakeDamage`(`:113~149`)는 HP 0 직전 `OnNearDeath`(ReviveHeal 아이템) 체크(`:116`) 후 `RuntimeStats.Damage`(`:132`)·`OnDamageTaken`(`:137`)만 발화. **HP가 0이어도 Die/EndRun/게임오버 없음.**
- `UI_Result`는 빈 스텁, `UIIds.GameOver=140`은 미사용. → **사망 감지·클리어 종료·복귀 전부 신규 작업.** 설계 가정과 일치.

**② BaseCamp 아바타·카메라 = PlayerController(CombatGirl) + Cinemachine 핸드오프로 확정.**
- 현 시작방은 이미 Wisp 단계를 제거하고 CombatGirl 몸을 즉시 스폰(`GameRunBootstrapper.cs:1813`). 선택대는 PlayerController 트리거로 동작.
- 카메라: `GameCameraController.HandToGameplayCamera(Transform follow)`(`GameCameraController.cs:170`)가 `CinemachineFreeLook.Follow/LookAt`를 플레이어로 설정(`:177-181`)하고 보간 인계. **null-safe**(FreeLook 부재 시 무동작). → BaseCamp는 PlayerController + `HandToGameplayCamera`. **Wisp 불필요.** (씬에 CinemachineFreeLook 리그 필요 = 에디터 체크리스트)

**③ `IsNewRunPending` = 사실상 죽은 플래그 → 재활용 가능.**
- `RequestStartRunAsync`에서 set(`AppBootstrapper.cs:174`)되지만 `ConsumeNewRunPending()`(`:189`)는 **호출처 0건**. 즉 set만 되고 아무도 읽지 않음.
- 실제 시작방 분기는 `IsInStartRoom = IsStartRoomScene && !Loadout.IsReady`(`GameRunBootstrapper.cs:55-56`)가 결정. **로드아웃이 준비된 채 진입하면 `IsInStartRoom=false`** → `StartRoomAsync`를 건너뜀(현재는 `StartCombatDirectAsync` fallback으로 빠짐, `:229`).
- → `IsNewRunPending`을 "BaseCamp→던전 직행" 신호로 **안전하게 재활용 가능**(PR2). 또는 `Loadout.IsReady` 상태만으로도 분기 전환 가능.

### 확정 전이 다이어그램

```
                         ┌──────────────────────────────────────────────┐
                         │  Lobby (UI_Lobby / UI_SaveSlotPanel)          │
                         └───────────────┬──────────────────────────────┘
            새 런 / 사망 후               │                 이어하기(hasSave)
   RequestStartRun → RequestStartRunAsync │                 RequestRestoreRun
   (AppBootstrapper.cs:165,170)           │                 (AppBootstrapper.cs:201)
   ★변경: RequestLoad(BaseCamp) :184      │                 (변경 없음)
                         ┌────────────────▼─────────────┐   ┌────────────────────────────┐
                  (a) →  │  BaseCamp (영속 허브 씬)       │   │  GameScene_ChN (던전 복원)   │
                         │  GameFlowState.BaseCamp        │   │  ContinueProcGenRunAsync     │
                         │  BaseCampBootstrapper.Start    │   │  (hasActiveRun&&!isInStartRoom)│
                         │   · 플레이어 스폰 + 카메라      │   │  마지막 방 재생성 → 전투      │
                         │   · NotifySceneReady           │   └────────────────────────────┘
                         │  [유물/무기/각성 선택 = PR2]    │
                         └────────────────┬───────────────┘
                  (b) →  BaseCampDungeonGate.EnterDungeon (PR1 스텁: RequestLoad(GameScene_Ch1))
                         PR2 목표: 서약선택 → StartProcGenRunAsync 직행(시작방 Zone0 생략, 앵커 (0,0,2000f))
                         ┌────────────────▼───────────────┐
                         │  GameScene_ChN (절차 던전 전투)  │  GameFlowState.InGame
                         │  RunFlowController.StartRunAsync │
                         └────────────────┬───────────────┘
                  (c) →  사망 EndRun(false) / 클리어 EndRun(true)  [PR3 신설 호출처]
                         → OnRunEnded → AppBootstrapper.HandleRunEnded (메타 저장, 기존)
                         → UI_Result (PR3 구현) → AppBootstrapper.EndRun() (세이브 폐기 :71)
                         → RequestLoad(BaseCamp)  ──────────► 다시 BaseCamp (a)
```

### 전이별 GameFlow·SceneTransition·세이브 분기

| 전이 | 트리거(코드 앵커) | GameFlow 상태 | SceneTransition | 세이브 분기 |
|---|---|---|---|---|
| **(a) 시작→BaseCamp** | `RequestStartRunAsync :184` ★`RequestLoad(BaseCamp)` | None/Lobby → **BaseCamp** | `RequestLoad`→`GameFlow.RequestLoad`(flow) 또는 `LoadSceneNoFlowAsync`(no-flow) | RPM `ClearAsync`로 슬롯 초기화(`:177`). 던전 세이브 미생성 |
| **(a') 이어하기→던전** | `UI_SaveSlotPanel.OnSlotStartClicked :128` (hasSave) → `RequestRestoreRun` | → **InGame** | `GetSceneForChapter` | `hasActiveRun && !isInStartRoom` → 던전 방 복원(변경 없음) |
| **(b) BaseCamp→던전** | `BaseCampDungeonGate.EnterDungeon`(PR1 스텁) | BaseCamp → **InGame** | PR1: `RequestLoad(GameScene_Ch1)` / PR2: 직행 | PR2: 게이트 통과 시 **최초 던전 세이브 생성**(isInStartRoom=false) |
| **(c) 사망/클리어→BaseCamp** | `EndRun(bool)` 신설 호출처(PR3) → `UI_Result` → `RequestLoad(BaseCamp)` | InGame → **BaseCamp** | `AppBootstrapper.EndRun()`(세이브 폐기) 후 `RequestLoad(BaseCamp)` | 던전 세이브 폐기 → BaseCamp 신규 진입 |

> **PR1 중간 상태(의도된 절충):** PR1은 (a)만 실제 전환하고 (b)는 "GameScene_Ch1 진입" 스텁이다. PR1 직후 새 런 흐름은 **Lobby → BaseCamp → [게이트] → GameScene_Ch1(기존 시작방 Zone0 그대로 실행) → 던전**이 되어 **허브가 2번**(BaseCamp + 기존 시작방) 나타난다. 이는 회귀 없이 새 전환만 검증하기 위한 의도적 중간 단계이며, **PR2에서 Ch1 시작방 경로를 비활성화**하면서 단일 허브로 정리된다.

---

## 1. 현재 구조 정밀 파악

### 1-1. 시작방 구성물과 생성 방식

| 구성물 | 생성 방식 | 근거 |
|---|---|---|
| **방 지오메트리(Zone 0)** | 절차생성 — ZoneLayout `zone_index==0`을 `SpawnStartZoneFromLayoutAsync`가 블록맵으로 빌드 | `GameRunBootstrapper.cs:509,541,567` |
| **유물 제단/캐릭터 픽업** | **토큰 스폰** — CP 타일 발견 순서대로 `startCharacterPickupPrefabs` 매핑 | `GameRunBootstrapper.cs:61` (주석), CP 타일 |
| **무기 선택대** | **토큰 스폰** — WP 타일 발견 순서대로 `startWeaponPickupPrefabs` 매핑 | `GameRunBootstrapper.cs:64` |
| **각성 제단** | **코드 스폰** — 플레이어 스폰 지점 + (4,0,2) | `GameRunBootstrapper.cs:1809,1821-1825` |
| **탈출 게이트** | **코드 배치** — Zone 0 출구 엣지에 `StartRoomGate` 부착 | `GameRunBootstrapper.cs:573-575,1012-1048` |
| **플레이어 아바타** | CombatGirl 몸 **즉시 스폰**(Wisp 단계는 코드상 제거됨) | `GameRunBootstrapper.cs:1813-1818`, `SpawnCharacterInStartRoomAsync :1864` |
| **Wisp** | `SpawnWisp()`(`:1848`) 정의는 있으나 `StartRoomAsync` 현행 경로에서 **미호출**(몸 즉시 스폰으로 대체) | `:1813` 주석 "위습 단계 제거" |
| **플레이어 스폰 위치** | `_pendingPlayerSpawnPos = worldCenter + (spawn_local_x, 0, spawn_local_z)` | `GameRunBootstrapper.cs:567-568` |

> **설계 함의:** 선택대(유물/무기)는 "씬에 박힌 오브젝트"가 아니라 **ZoneLayout 토큰(CP/WP)에서 스폰**된다. BaseCamp 손배치로 옮기면 이 토큰 의존을 끊고 **프리팹 직접 배치**로 전환해야 한다(2-1 참조). 각성제단은 이미 `WorldAwakeningAltar.SpawnAt`로 코드 스폰이라 어느 쪽이든 쉽다.

### 1-2. 진입 흐름 (신규 런)

```
UI_Lobby.OnClickStartRun (UI_Lobby.cs:49)
  → AppBootstrapper.RequestStartRun (AppBootstrapper.cs:165)
  → (인트로 영상) → RequestLoad(GameScene_Ch1) (:185)
GameScene_Ch1 로드
  → GameRunBootstrapper.Start (:185~232)
     · 데이터 매니저 init → IsStartRoomScene이면 SpawnWorldMap 생략(:206)
     · 분기(:225~230):
         _run.IsRunning            → ContinueProcGenRunAsync (이어하기)
         IsInStartRoom             → StartRoomAsync()  ← 신규 런 = 시작방
         else                      → StartCombatDirectAsync (에디터 fallback)
  → StartRoomAsync (:1792)
     ScreenFade.Out → SpawnStartZoneFromLayoutAsync → 카메라 투어
     → SpawnAwakeningAltar → 대화 → CombatGirl 몸 스폰
  → (플레이어가 유물/무기 픽업 선택)
  → StartRoomGate 진입 (StartRoomGate.cs:244~320)
     IsLoadoutReady() 확인 → ExitStartRoomAsync (:324)
       → ShowCovenantChoiceAsync (:340, 무작위 3개 서약)
       → bootstrapper.StartProcGenRunAsync (:335)
         → flow.StartRunAsync((0,0,2000f), poolKey) (:719)  ← 던전 첫 방 격리 빌드
```

- `IsInStartRoom = IsStartRoomScene && IsZoneLayoutMode && 로비경유`(`GameRunBootstrapper.cs:50-59`). `startWithZoneLayout=true`가 기본(`:33`).
- 게이트 모드 분기: `_fromZoneIndex == -1`이면 **시작방 모드**(로드아웃 게이팅), 그 외는 존 전환 모드(`StartRoomGate.cs:47,109,133,244,256`).

### 1-3. 사망/클리어 복귀 흐름 (현재 = 미배선)

| 단계 | 상태 | 근거 |
|---|---|---|
| 플레이어 사망 감지(Hp 0 → EndRun) | ❌ 미구현 | `PlayerController.TakeDamage`(`:92`)는 HP 감산·VFX만, EndRun 호출 없음 |
| `GameRunSession.EndRun(isCleared)` | ⚠️ 정의됨, **호출처 미발견** | `GameRunSession.cs:313`, `OnRunEnded` 발화 |
| `OnRunEnded` 구독 | ✅ 배선 | `AppBootstrapper.BeginRun`(`:62-69`) → `HandleRunEnded`(`:97`) → `BackendGameData.ApplyRunResultAsync` (메타 저장만) |
| 결과 UI | ❌ 빈 스텁 | `UI_Result.cs`(Init만), `UIIds.cs:57` `GameOver=140` |
| 사망/클리어 후 씬 전환 | ❌ 미배선 | 어느 곳도 `RequestLoad(Lobby/BaseCamp)`를 런 종료 경로에서 호출 안 함 |

> **확인 필요:** 보스/최종방 클리어가 `EndRun(true)`를 호출하는 경로가 정말 전무한지(에이전트 Grep 기준 미발견). RoomClear/Boss 컨트롤러 쪽에 숨은 호출이 있을 수 있어 PR3 착수 시 재확인.

### 1-4. 씬/부트 템플릿

- **GameFlow:** `GameFlowState.BaseCamp` enum 존재(`GameFlow.cs:9`), `Define.Scene.BaseCamp→BaseCamp` 매핑(`:28`). 전환 자체는 `RequestLoad(scene)`(`:49`) → `SceneTransitionManager.LoadScene` → `ChangeState`.
- **AppBootstrapper:** `ApplyUIForState`의 `case BaseCamp:`(`:569`) 빈 분기 — "BaseCampBootstrapper가 NotifySceneReady 호출" 주석만. 즉 **씬 로드는 되지만 초기화 주체가 없음**.
- **Bootstrapper 패턴(템플릿):** `GameRunBootstrapper.Start`(`:185`)가 표준 — ① `AppBootstrapper.IsReady` 대기(`:192`) ② 데이터 매니저 init ③ 씬 콘텐츠 빌드 ④ `AppBootstrapper.Instance?.NotifySceneReady()`(`:232`)로 로딩 UI 해제. BaseCampBootstrapper도 이 골격을 그대로 따른다.

### 1-5. 세이브 정합 (PR1 이어하기와 BaseCamp 관계)

`RunSaveData`(`RunSaveData.cs:14`) 핵심 필드:
- `hasActiveRun`(`:17`), `isInStartRoom`(`:35`, "스타트룸 미퇴장"), `currentZoneIndex`(`:36`), 절차생성 복원용 `masterSeed/visitCount/seqPhase`(`:51-53`), 챕터·HP·골드·로드아웃·인벤토리·서약·룬보드 등.

현행 복원 분기(`AppBootstrapper.RequestRestoreRunAsync :206`):
- `!hasActiveRun` → 실패 콜백
- `isInStartRoom==true` → **Loadout.Clear 후 새 런으로**(`:220-228`) — 시작방 미퇴장 저장은 의미 없으므로 폐기
- 그 외 → 세션 복원 → `GetSceneForChapter`로 던전 재진입(`ContinueProcGenRunAsync`로 마지막 방 재생성)

> **BaseCamp 승격 후 세이브 분기(목표):**
> - **새 런 / 사망 후** → BaseCamp(영속 씬). **던전 세이브 없음/폐기.**
> - **이어하기(`hasActiveRun && !isInStartRoom`)** → 기존대로 **던전 방 복원**(BaseCamp 미경유). ← PR1 이어하기와 충돌 없음.
> - `isInStartRoom`의 의미는 사실상 "BaseCamp에 있고 아직 게이트 미통과" = **던전 세이브를 만들지 않은 상태**로 단순화 가능. 권고: **BaseCamp 게이트 통과 시점에 최초 던전 세이브 생성**. 그 전 종료 = 세이브 없음 → 다음 실행 시 BaseCamp 신규 진입.

---

## 2. BaseCamp MVP 설계

### 2-1. 씬 구성 설계 (`BaseCamp.unity`)

손배치 영속 씬. 좌표는 **원점 기준 로컬**(던전이 별도 씬이 되므로 2000f 격리 불필요 — 2-3 참조).

```
BaseCamp.unity
├─ Environment
│   ├─ Floor (Plane/ProBuilder, Collider)         ← 신규
│   ├─ Directional Light                            ← 기존 재사용
│   └─ (NavMesh Surface)                            ← Merlin 이동 시에만, MVP는 정적이면 생략 가능
├─ Player Spawn Point (Transform marker)            ← 신규, BaseCampBootstrapper가 참조
├─ Main Camera (+ WispCameraFollow 또는 GameCameraController)  ← 카메라 추적 재사용
├─ Interactables
│   ├─ RelicAltar (유물 선택대)                      ← StartRoomPickup/CharacterDisplayStand 프리팹 직접 배치
│   ├─ WeaponDisplayStand (무기 선택대)              ← WeaponDisplayStand 프리팹 직접 배치
│   ├─ AwakeningAltar (각성 제단)                     ← 기존 BaseCamp.unity 오브젝트 재사용/재배치
│   ├─ SoulRestorationAltar (영혼 복원 제단)          ← ⏸ 결정 대기(분기 A) — 자리만 확보
│   ├─ MerlinNPC (경량)                              ← 신규, IWispInteractable 또는 트리거+F
│   └─ SettingsTerminal (설정 진입점)                 ← 기존 UI_Settings 연결, 또는 HUD 버튼으로 대체
└─ DungeonGate (던전 입장 게이트)                     ← StartRoomGate 로직 이식(또는 신규 BaseCampGate)
```

**배치 방식 결정: 토큰 스폰 → 프리팹 직접 배치.**
- 이유: BaseCamp는 절차생성이 아닌 **고정 영속 씬**이므로 ZoneLayout/CP·WP 토큰 의존이 불필요·부적합. 선택대를 인스펙터로 직접 배치하면 디자인 통제·로딩 단순화.
- 각성 제단: `WorldAwakeningAltar`는 이미 코드 스폰 가능(`SpawnAt`) — BaseCamp에선 **씬에 프리팹으로 직접 배치**(BaseCamp.unity에 이미 1개 존재하므로 위치만 조정).

**아바타 결정: PlayerController(CombatGirl) 직접 스폰 권고.**
- 근거: 현행 StartRoom이 이미 Wisp 단계를 제거하고 CombatGirl 몸을 즉시 스폰(`GameRunBootstrapper.cs:1813`). 선택대(Character/Weapon DisplayStand)는 PlayerController 트리거로 동작, 각성제단은 자체 트리거+F. → **Wisp 불필요.**
- 대안(확인 필요): 유물 선택 전 "외형 없는 요정" 연출을 원하면 Wisp 스폰(`SpawnWisp :1848`) 재활용 가능. 단 선택대 상호작용 주체(Wisp vs Player)를 통일해야 함. **MVP는 PlayerController 단일 아바타로 단순화.**

### 2-2. BaseCampBootstrapper 설계

`GameRunBootstrapper` 패턴을 따른 경량 부트스트래퍼. **신규 클래스.**

```csharp
// Assets/RelicFairy/Systems/Bootstrapper/Scripts/BaseCampBootstrapper.cs (신규)
public sealed class BaseCampBootstrapper : MonoBehaviour
{
    // [SerializeField] private only — 인스펙터 노출 금지 규칙 준수
    [Header("Spawn")]
    [SerializeField] private Transform playerSpawnPoint;
    [SerializeField] private string    playerBodyKey = "PlayerCharacter"; // startBodyKey와 동일 키
    [SerializeField] private BaseCampGate dungeonGate;                    // 또는 StartRoomGate 재사용

    // Lifecycle: Awake → Start
    private async void Start()
    {
        // 1) AppBootstrapper 준비 대기 (GameRunBootstrapper.cs:192 동일 패턴)
        await UniTask.WaitUntil(() => AppBootstrapper.Instance == null || AppBootstrapper.Instance.IsReady);

        // 2) 메타 데이터 init (각성/유물 등 — 허브에서 소비할 데이터)
        //    Managers.RelicAwakening.InitializeAsync() 등 (GameRunBootstrapper.InitRelicAwakeningAsync 참고)

        // 3) 카메라 + 플레이어(CombatGirl) 스폰 → 선택대/제단은 씬 배치라 스폰 불필요
        //    SpawnCharacterInStartRoomAsync 패턴 일부 재사용(InitAsync 대기, 카메라 핸드오프)

        // 4) HUD 모드: 허브 전용(전투 HUD 억제). SetHudStartRoomSuppressed 패턴 참고
        //    UIRootBootstrapper.Instance?.SetHudStartRoomSuppressed(true) (GameRunBootstrapper.cs:1795)

        // 5) 로딩 UI 해제
        AppBootstrapper.Instance?.NotifySceneReady();
    }
}
```

**AppBootstrapper 빈 분기 채우기:** `ApplyUIForState`의 `case GameFlowState.BaseCamp:`(`AppBootstrapper.cs:569`)는 현행 Tutorial/InGame과 동일하게 **"Bootstrapper가 NotifySceneReady 호출"** 패턴이면 되므로, 분기 본문은 비워두고 BaseCampBootstrapper가 `NotifySceneReady()`를 호출하도록 두면 충분(코드 수정 최소). 단 허브 전용 메뉴 UI가 필요하면 여기서 `Managers.UI.ShowMenuUI<...>()` 추가.

> **확인 필요:** 카메라 — `WispCameraFollow`는 Wisp 추적용. PlayerController 아바타면 `GameCameraController.HandToGameplayCamera`(`GameRunBootstrapper.cs:1893`)를 그대로 쓸지, 허브 전용 고정/추적 카메라를 둘지 결정. MVP는 게임플레이 카메라 핸드오프 재사용 권고.

### 2-3. 진입/복귀 흐름 재배선

**(a) 게임 시작: Lobby → BaseCamp**
- `UI_Lobby.OnClickStartRun`(`UI_Lobby.cs:49-55`) 변경:
  - `hasActiveRun && !isInStartRoom`(던전 진행중) → 기존 `RequestRestoreRun()`(던전 복원)
  - 그 외(신규/사망후) → **`RequestLoad(Define.Scene.BaseCamp)`** (기존 `RequestStartRun`의 Ch1 직행을 BaseCamp로 교체)
  - 인트로 영상(`GameStartVideoPlayer`)은 최초 1회 BaseCamp 진입 전 유지 가능.
- > 확인 필요: `UI_Lobby.cs:50-54`의 이어하기 분기 정확 조건(현재 if/else 한 줄만 확인). PR3에서 정밀 확인.

**(b) BaseCamp → 던전 입장**
- DungeonGate(=StartRoomGate 이식 또는 BaseCampGate 신규):
  - `IsLoadoutReady()`(`StartRoomGate.cs:408`) 폴링 → 준비 시 포탈 활성(`portalActive`)
  - 진입 시 `ShowCovenantChoiceAsync`(`:340`, 서약 선택) → **`RequestLoad(GameScene_Ch1)`**
  - 씬 로드 후 GameRunBootstrapper가 **시작방을 건너뛰고 던전 직행**(아래 (d)/2-4)
- 서약 선택 시점: 현행처럼 게이트 통과 직전에 유지(분기 B와 무관하게 런 서약 선택은 그대로 동작).

**(c) 사망/클리어: 던전 → BaseCamp 복귀 (신규)**
1. **EndRun 호출처 신설**:
   - 사망: 플레이어 HP 0 감지 → `GameRunSession.EndRun(false, "death")`. 감지 위치는 `PlayerRuntimeStats`/`RunSession` HP 0 시점(확인 필요: 적절한 단일 지점).
   - 클리어: 보스/최종방 클리어 → `EndRun(true)`. (확인 필요: 기존 RoomClear/Boss 컨트롤러의 종료 신호와 연결)
2. `OnRunEnded` → `AppBootstrapper.HandleRunEnded`(기존 메타 저장) **이후 씬 전환 추가**:
   - `UI_Result`(최소 구현) 표시 → 버튼 → `AppBootstrapper.EndRun()`(세이브 정리 `:71`) → **`RequestLoad(Define.Scene.BaseCamp)`**
3. GameFlow 상태: InGame → (Result UI 오버레이) → BaseCamp 전환. `SceneTransitionManager.LoadScene` 표준 경로.

**(d) 시작방 직행 제거 → 던전 직접 빌드**
- GameScene_Ch1의 `GameRunBootstrapper.Start` 분기(`:225-230`)에서 **신규 런이 시작방(StartRoomAsync)으로 가지 않도록** 변경:
  - BaseCamp에서 로드아웃 확정 후 진입한 신규 런 = **"던전 직행 모드"** → `StartProcGenRunAsync()`(`:705`)를 바로 호출(시작방 Zone 0 빌드 생략).
  - 신호: 기존 `AppBootstrapper.IsNewRunPending`/`ConsumeNewRunPending`(`:60,190`) 재활용 가능성 검토(확인 필요: 현재 소비 지점). 또는 BaseCamp 게이트가 "loadout ready + new run" 플래그를 세팅.
- `startWithZoneLayout`(`:33`)는 **시작방 용도로는 비활성** 또는 Zone 0을 던전 첫 방으로 대체. **던전 앵커는 별도 씬이 되어 허브와 충돌하지 않으므로 (0,0,2000f) 오프셋을 원점으로 되돌릴 수 있음**(선택적 정리, MVP는 2000f 유지해도 무해).

### 2-4. 시작방 제거/이전 체크리스트

| 현행(Ch1 내부) | 이전 후(BaseCamp) | 작업 |
|---|---|---|
| Zone 0 절차 빌드(`SpawnStartZoneFromLayoutAsync`) | BaseCamp 손배치 씬 | Ch1에서 Zone0=시작방 경로 비활성, BaseCamp 지오메트리 작성 |
| CP/WP 토큰 → 캐릭터/무기 픽업 | RelicAltar/WeaponDisplayStand 직접 배치 | 프리팹 인스펙터 배치 + 게이트 참조 주입 |
| 코드 스폰 각성제단 | 씬 배치 각성제단 | BaseCamp.unity 기존 제단 위치 조정 |
| Zone 0 출구 StartRoomGate | BaseCamp DungeonGate | StartRoomGate 이식 or BaseCampGate 신규 |
| `StartRoomAsync` 진입 | `BaseCampBootstrapper.Start` | 신규 부트 |
| 던전 첫 방 (0,0,2000f) | 던전 첫 방 (원점 또는 2000f) | 별도 씬화로 오프셋 선택적 제거 |

> **위험:** `StartProcGenRunAsync`(`:711`)는 시작방 흐름이 세션 챕터 미설정(0)일 수 있어 **씬 이름에서 챕터 유추**(`ResolveCurrentChapter :722`)에 의존. BaseCamp→Ch1 진입 시에도 이 유추가 성립해야 함(GameScene_Ch1 씬명 유지 → OK). 단 BaseCamp에서 "어느 챕터로 갈지"를 명시 전달하는 편이 안전(확인 필요: 멀티 챕터 분기 시).

### 2-5. 세이브 정합 (요약)

```
신규 런 시작        → BaseCamp 진입.        던전 세이브 미생성
BaseCamp 게이트 통과 → 던전 첫 방 빌드 시점에 최초 던전 세이브 생성(isInStartRoom=false)
던전 진행 중 종료    → 이어하기: 던전 방 복원(ContinueProcGenRunAsync), BaseCamp 미경유
사망/클리어          → AppBootstrapper.EndRun(세이브 폐기) → BaseCamp 진입
```
- PR1 이어하기와 **충돌 없음**: BaseCamp는 "새 런/사망 후" 진입점, 이어하기는 던전 복원 경로 그대로.
- `isInStartRoom` 필드: 의미 축소(=게이트 미통과). 기존 복원 분기(`AppBootstrapper.cs:220`)는 그대로 "초기화 후 새로 시작" 처리하면 BaseCamp 신규 진입으로 귀결(정합).

---

## 3. 단계별 구현 PR 제안

각 PR은 **독립 검증 가능**하도록 분할.

### PR1 — BaseCamp 씬 골격 + Bootstrapper + Lobby 진입
- 내용: `BaseCamp.unity`에 바닥/조명/스폰포인트/카메라/플레이어(CombatGirl) 스폰. `BaseCampBootstrapper` 신규. `AppBootstrapper` BaseCamp 분기 활성(NotifySceneReady 경로). Lobby StartRun → BaseCamp 로드(이어하기 분기 보존).
- 임시: 던전 게이트는 "즉시 GameScene_Ch1 던전 직행"(로드아웃/서약 생략)으로 스텁 → 흐름만 검증.
- **검증:** Lobby→BaseCamp 진입, 플레이어 이동, 게이트→던전 진입, 로딩 UI 정상 해제. 콘솔 에러 0(`refresh_unity`→`read_console`).
- 위험: 카메라 추적 주체(Wisp vs Player) 미스매치 → 검은 화면. **확인 필요 지점.**

### PR2 — 시작방 구성물 이전 (선택대/제단/게이트)
- 내용: RelicAltar/WeaponDisplayStand/AwakeningAltar **직접 배치**. DungeonGate에 `IsLoadoutReady` 게이팅 + 서약 선택 이식. GameScene_Ch1의 **Zone 0 시작방 경로 비활성**(던전 직행 모드).
- **검증:** BaseCamp에서 유물+무기 선택 → 게이트 활성 → 서약 선택 → 던전 첫 방 정상 빌드. 미선택 시 게이트 거부. Ch1 단독 실행 시 회귀 없음.
- 위험: CP/WP 토큰 의존 제거가 다른 챕터(Ch2~4) 시작 흐름에 영향(확인 필요: 챕터별 시작방 동작). 던전 앵커 오프셋 정합.

### PR3 — 사망/클리어 → BaseCamp 복귀 + 결과 UI
- 내용: 사망 감지(HP0→`EndRun(false)`), 클리어 신호(→`EndRun(true)`) 연결. `UI_Result` 최소 구현(클리어/사망 표시 + "베이스캠프로" 버튼). `OnRunEnded` 후 `RequestLoad(BaseCamp)` 배선.
- **검증:** 던전에서 사망→결과→BaseCamp 복귀; 클리어→결과→BaseCamp 복귀; 메타(정수/골드) 정산 1회 적용; 세이브 폐기 후 재진입 시 BaseCamp 신규.
- 위험: EndRun 중복 호출/이벤트 누수. 보스 클리어 기존 신호와 충돌(확인 필요: 숨은 EndRun 경로).

### PR4 — 영혼 복원 제단 (⏸ 결정 대기: 분기 A 통화 네이밍)
- 내용: `SoulRestorationAltar` + `UI_RestorationPanel`(각성 제단 `WorldAwakeningAltar`/`UI_AwakeningPanel` 복제) — 통화 소비→유물/룬/서약 풀 **영속 해금**→런 풀 반영.
- 선결: `UserGameData`에 해금 집합 필드 신설(`unlockedRelics` 등 — 현재 전무), 통화 네이밍(Abyss Essence 리네이밍 vs 신규 통화).
- **검증(결정 후):** 파편 소비→해금 영속 저장(뒤끝)→다음 런 선택 풀 확장.

### PR5 (선택) — 멀린 NPC + 설정 진입점 폴리시
- 경량 대화(말 걸기+1줄), 설정 진입(기존 `UI_Settings` 연결). 확장: 진행도 기반 대사 분기.

---

## 4. 기존 자산 재활용 vs 신규

| 항목 | 재활용 | 신규 |
|---|---|---|
| 부트 패턴 | `GameRunBootstrapper.Start` 골격 | `BaseCampBootstrapper` |
| 플레이어 스폰/카메라 | `SpawnCharacterInStartRoomAsync`, `HandToGameplayCamera`, `WispCameraFollow` | 스폰 포인트 마커 |
| 선택대 | `CharacterDisplayStand`/`WeaponDisplayStand`/`StartRoomPickup` 프리팹 | 토큰→직접배치 전환 |
| 각성 제단 | `WorldAwakeningAltar`+`UI_AwakeningPanel` 전부 | 위치 조정만 |
| 던전 게이트 | `StartRoomGate`(ExitStartRoom/Covenant/IsLoadoutReady) | `BaseCampGate`(또는 StartRoomGate 모드 확장) |
| 서약 선택 | `ShowCovenantChoiceAsync`+`UI_CovenantChoice` | — |
| 세이브 | `RunProgressManager`/`RunSaveData`/`ContinueProcGenRunAsync` | isInStartRoom 의미 축소, 게이트 통과시 최초 세이브 |
| 결과/복귀 | `GameRunSession.EndRun`/`OnRunEnded`/`AppBootstrapper.EndRun` | EndRun 호출처(사망/클리어), `UI_Result` 구현, BaseCamp 복귀 배선 |
| 영혼 복원 제단 | 각성 제단 템플릿 | 통화·해금 데이터·UI (PR4, 결정 대기) |
| 멀린 NPC | `IWispInteractable`+대화 UI | 멀린 프리팹/대사 |

---

## 5. 결정 대기 / 확인 필요 목록

**결정 대기(기획):**
- 분기 A — 메타 통화 네이밍(`Abyss Essence` 리네이밍 vs `영혼 파편` 신규). PR4 선결.
- 분기 B — 서약 Ascension화(런 버프 유지 vs 영속 난이도). BaseCamp 서약 관리대는 MVP 제외, 분기 B 확정 후.

**확인 필요(코드):**
1. 사망/클리어가 `EndRun()`을 호출하는 숨은 경로 존재 여부(현재 미발견 — 1-3, PR3).
2. `UI_Lobby.cs:50-54` 이어하기 분기 정확 조건(2-3a).
3. BaseCamp 아바타 카메라 주체(Wisp vs PlayerController) 및 추적 방식(2-2, PR1).
4. `AppBootstrapper.IsNewRunPending` 소비 지점 — "던전 직행 모드" 신호로 재활용 가능한지(2-3d).
5. Zone 0 시작방 경로 비활성이 Ch2~4 시작 흐름에 미치는 영향(2-4, PR2).
6. 각성 레벨의 런 중 전투 스탯 적용 코드 존재 여부(허브에서 소비하는 데이터의 실효성).
```

