# 정적 플레이 시뮬레이션 리포트 — Project RelicFairy

- 작성: 2026-07-04 (파일명은 요청대로 `playthrough-sim-2026-07-02.md`)
- 방식: **읽기 전용 정적 코드 추적**. 플레이어 1명이 부팅→베이스캠프→던전→보스→챕터전환→런종료→이어하기까지 실제 플레이한다고 가정하고, 각 전이가 실제 코드로 성립하는지 파일을 열어 확인.
- 조사: 하위 에이전트 4구간 병렬(A: 부팅/로드아웃, B: 인게임진입/방루프, C: 상점/보스, D: 챕터전환/종료/이어하기) + 오케스트레이터 직접 재검증(핵심 블로커·시드 정합·런종료).
- 컴파일 상태: **에러 0 / 경고 0** (Unity 콘솔, 마지막 1건은 MCP 웹소켓 인프라 로그로 코드 무관).
- 헤드리스 한계: 씬 배치·Addressable 등록·CDN 데이터 존재는 코드로 100% 단정 불가 → "확인필요(인게임)"로 표기.

---

## 결론 (엔드투엔드 완주 가능 여부)

**코드 배선 수준에서 부팅→런클리어/사망→허브복귀→이어하기까지 단절 없이 연결되어 완주 가능.**
정적 분석에서 실행을 막는 확정 🔴블로커는 **없음**. 남은 위험은 전부 **데이터/에셋 가용성 의존(⚠️조건부)** 이며 인게임 1회 실행으로 검증 필요.

> 주의: 조사 초기 하위 에이전트 B가 "Phase=Running 미설정"을 🔴블로커로 보고했으나, 직접 재검증 결과 **이미 수정된 상태**(오탐). 아래 스텝 3 참조.

---

## 스텝별 판정 요약표

| 스텝 | 구간 | 판정 | 핵심 근거 |
|---|---|---|---|
| 1 | 부팅 (AppBootstrapper→IsReady, EventSystem) | ✅통과 | `AppBootstrapper.cs:465~565`, `EventSystemBootstrapper.cs:19~36` |
| 2 | 베이스캠프/온보딩/로드아웃/던전입장 | ✅통과 | `BaseCampBootstrapper.cs:47~97`, `BaseCampOnboardingDirector.cs:74~225`, `BaseCampDungeonGate.cs:47~54` |
| 3 | 인게임 진입 라우팅 + Phase=Running + 자동저장 | ✅통과 | `GameRunBootstrapper.cs:2155~2157`, `GameRunSession.cs:231/313`, `RunFlowController.cs:354~356` |
| 4 | 방 루프(결정적 진행)·전투·처치·보상 | ✅통과 | `RunSequencer`, `RoomWaveController`, `MonsterBase.cs:739~817`, `RunFlowController.cs:457~468` |
| 5 | 상점방 NPC UI + 버프뷰 | ✅통과 / ⚠️조건부 | `ShopRoomController.cs:161~182`, `UI_ShopPanel`, `BuffViewAggregator`/`HudPresenter` |
| 6 | 보스방 스폰·연출·처치→챕터전진 | ✅통과 / ⚠️조건부 | `BossSpawner`, `BossRoomController`, `ChapterGate.cs:99~110`, `GameRunBootstrapper.cs:821~850` |
| 7 | 챕터 전환 Ch1→Ch4 + 최종클리어 | ✅통과 | `ChapterRegistry.asset:22(_finalChapter=4)`, `GameRunSession.cs:510~539` |
| 8 | 런 종료(사망/클리어)→세이브삭제→허브 | ✅통과 | `GameRunBootstrapper.cs:857~891`, `AppBootstrapper.cs:75~83` |
| 9 | 이어하기 시드/visitCount 정합 | ✅통과 | `RunFlowController.cs:119~160/298`, `GameRunSession.RestoreFromSaveAsync:313` |

---

## 스텝 상세

### STEP 1 — 부팅 ✅통과
- `AppBootstrapper.Start()`: Managers 확인 → `addr.InitAsync()`(null 가드) → `EnsureUIRootAsync()`+`SetRoots()` → `DeviceAutoLoginAsync()` → 데이터 초기화(Item/Rune/RelicStat) → `_flow.RequestLoad(startScene)` → `IsReady=true`. 근거 `AppBootstrapper.cs:465~565`.
- **EventSystem 보장**: `EventSystemBootstrapper.cs:19~36` — `[RuntimeInitializeOnLoadMethod(BeforeSceneLoad)]`로 `sceneLoaded` 훅 등록, 씬마다 `FindFirstObjectByType<EventSystem>()` 없으면 `@EventSystem`(EventSystem+StandaloneInputModule) 생성. **모든 씬에서 UI 클릭 보장**, 무한대기/NRE 불가.
- UIRoot/HUD: `UIRootBootstrapper.cs:28~59`(DDOL·중복가드·BindHudToRun·HUD억제토글), `HudBootstrapper` `_constructed` 중복 Construct 방지.
- ⚠️조건부: Addressables/Backend 초기화 실패 시 조기 return 또는 경고만 → UIRoot 미생성 가능(폴백은 경고 로그 수준). 자동로그인 실패는 **설계상 오프라인 진행 허용**(정상).

### STEP 2 — 베이스캠프/로드아웃/던전입장 ✅통과
- `BaseCampBootstrapper.Start()`: `WaitUntil(AppBootstrapper.IsReady)` → 카메라 → HUD억제 → `SpawnPlayerAsync` → `NotifySceneReady` → 진입대사 → 게이트. 근거 `BaseCampBootstrapper.cs:47~97`.
- **온보딩(신규)**: `BaseCampOnboardingDirector` — `QuestEvents.OnReported` 구독(74), `OnboardingEntranceTrigger`가 진입 시 `OnEntered` 호출, 단계별 배리어(`OnboardingBarrierDome`) 해제, 1회차 완료 시 `PlayerPrefs`로 스킵. **퀘스트 이벤트 구동 완전 자동화, 흐름 파괴 없음.** ⚠️`WaitUntil(Managers.Player…)` 매프레임 재검사라 스폰 지연 자체 완화.
- 로드아웃 3종: `RelicAltar.Claim→SetRelic`(81), `WeaponForgeAltar→SetWeaponSlot0/1`(131~137), `CovenantPickup→AddCovenant`(54~57). 게이팅 `BaseCampDungeonGate.cs:47~54`(유물&무기&서약≥1) → `EnterDungeon`→`MarkNewRunPending()`+`RequestLoad(GameScene_Ch1)`.
- 확인필요(인게임): 씬에 온보딩 트리거/제단/서약 픽업 실제 배치, `UI_WeaponForgePopup` Addressable 등록.

### STEP 3 — 인게임 진입 라우팅 ✅통과 (⚠️ 초기 오탐 정정)
- `GameRunBootstrapper.Start()`: `IsRunning && chapterAdvance`→`StartNextChapterInSceneAsync`, `IsRunning`→`ContinueProcGenRunAsync`(이어하기), 그 외(허브 신규)→대기방 흐름. 근거 `:239~257`.
- **Phase=Running (과거 버그, 현재 수정 확인)**: 허브 신규 런은 `StartWaitingRoomAsync()`가 `if(!_run.IsRunning) await _run.StartNewRunAsync(...)` 호출 → `GameRunSession.cs:231`에서 `Phase=Running`. 근거 `GameRunBootstrapper.cs:2155~2157`, 주석 2152~2154가 "챕터게이트 통과 시 AdvanceToNextChapter가 !IsRunning으로 false 반환하던 버그 차단"이라 명시. 이어하기 경로도 `RestoreFromSaveAsync:313`에서 Phase=Running. **→ 메모리 `run_phase_not_running_hub_bug`는 해소됨.**
- 자동저장: `RunFlowController.cs:354~356` 방 경계마다 `SaveRunState()`→`RunProgressManager.SaveRunLocal()`.

### STEP 4 — 방 루프 + 전투 ✅통과
- 결정적 진행: `RunSequencer`(마스터 시드 기반 깊이별 출구 종류 산출, 드리프트 검증 포함).
- 전투 체인: `MonsterBase.TakeDamage()`(`:739`) → HP0 → `OnFatalDamage()`+`RaiseDied()`(`:706/150`) → `OnDied` 구독(`RoomWaveController`/`RoomClearController`). 킬 콜백 `CovenantHandler.OnKill`/`EffectManager.OnKill`(아이템드롭)/`FirePassive(OnKill)`(`:773~778`).
- 클리어→다음방: `RunFlowController.HandleRoomCleared()`(`:457`)→`RollExits()`→`RevealGates()`→`EnterRoomAsync`→`SaveRunState()`. `RoomClearGate.Activate`로 포탈 배치.
- ⚠️조건부: (a) NavMesh 빌드 실패 시 조용히 스킵 → 몹 미스폰 가능, (b) 스포너 0마리 시 `LogError`(`RoomWaveController:235`) 후 거짓 클리어/게이트 미활성 → 진행 불가 재입장 필요. 둘 다 인게임 확인 권장.

### STEP 5 — 상점방(신규 NPC UI) + 버프뷰 ✅통과 / ⚠️조건부
- NPC: `ShopRoomController.SpawnNpc()→Instantiate(npcPrefab)`+`ShopNpcInteraction`, `OnInteract(F키)→OpenShopUIAsync→Managers.UI.ShowPopupUIAndGetAsync<UI_ShopPanel>`. 구매/환불/골드갱신/이벤트훅/언훅 전부 배선(구매 `PurchaseFromEntry→AddToStaging`, 골드 `OnGoldChanged→OnShopChanged`).
- 리롤: 기본 off(`RerollEnabled` 플래그), on 시 비결정 RNG 재구성.
- **버프뷰(신규)**: `BuffViewAggregator`가 방버프(`run.BuffHandler`)+동적소스(`PlayerBuffViewSource`/`ItemBuffViewSource`) 수집, `HudPresenter`가 이벤트 즉시갱신+0.25s 폴링. null 가드/옵셔널 체이닝 완비. **HUD 재구성/버프뷰가 흐름 깨지 않음.**
- ⚠️조건부: (a) `npcPrefab==null`(shopNpcAddressableKey 미로드) 시 경고만 → UI 못 엶, (b) `LuckRollTableSO`/`ShopDataManager` 미초기화 시 `BuildSlotsFromCatalog` 레거시 폴백(등급 롤 약화), (c) `EffectManager` 미초기화 상태의 `ItemBuffViewSource` NRE 저위험. 인게임 확인.

### STEP 6 — 보스방 ✅통과 / ⚠️조건부
- 스폰: `BossSpawner.TrySpawn()` — `ResolveBossSpawnTable() ?? spawnTable`(챕터 레지스트리 우선, 직렬화 폴백), `table.PickRandom(IsBossEntry)`→`AddressableManager.InstantiateAsync`. 배치보스는 `ActivatePlacedBossAsync→SetActive`+디졸브.
- 입장연출: `IBossEntrance` 구현 보스는 카메라 팬→`TriggerEntrance`→`OnCombatReady`→입력복구. 미구현 보스는 즉시 입력복구(안전 회피).
- 처치→전진: `BossRoomCleared`→`ChapterGate.Spawn`(`GameRunBootstrapper:196~199`)→플레이어 진입 `ChapterGate.cs:99~110`→`AdvanceChapter()`.
- **메모리 "Ch1 보스 이중 등장"**: 현재 코드상 배치보스는 `SetActive`가 비활성 시에만 동작, 스폰테이블 경로는 격리배치 아님 → **코드상 이중 출현 경로 확인 안 됨**(스텁/구버전 메모리 가능성). ⚠️확인필요(인게임).
- ⚠️조건부: `ChapterRegistry` 미할당 시 `BossSpawner.spawnTable` 직렬화값 필수, 풀 entry의 `addressableKey` 유효성. 인게임 확인.

### STEP 7 — 챕터 전환 Ch1→Ch4 ✅통과
- 씬 존재: `GameScene_Ch1~Ch4.unity`(CLAUDE.md 및 씬 폴더). `GetSceneForChapter(chapter)`로 로드.
- **게이팅 정정**: `ChapterRegistry.asset:22 _finalChapter: 4`. `HasNextChapter()=CurrentChapter+1<=FinalChapter`, `AdvanceToNextChapter()`(`GameRunSession.cs:510~525`). **→ 메모리 `ch1_complete_demo`의 "_finalChapter=Ch1 승리 게이팅"은 stale, 현재 Ch1→Ch4 전 구간 도달 가능.**
- 오프바이원 방지: `EnsureChapter()`(`:534~539`)가 절차 흐름의 미설정(0) 챕터를 런 시작 시 1회 확정.
- 전환: `AdvanceChapter()`(`:821~850`) → `EnterChapterClear`+`AdvanceToNextChapter` → 비최종이면 `MarkChapterAdvance`+`RequestLoad(GetSceneForChapter)` → 새 씬 `StartNextChapterInSceneAsync`(대기방→게이트→던전). 최종이면 `HandleRunClear`.
- ⚠️확인필요: Ch2~4 보스 스폰테이블/룸풀(CDN 또는 SO 폴백) 실제 채워졌는지. 코드 폴백 체인은 성립.

### STEP 8 — 런 종료 ✅통과
- 진입점: `HandlePlayerDeath→HandleRunEndAsync(false)`, `HandleRunClear→HandleRunEndAsync(true)`(`:809/812`).
- 시퀀스(`:857~891`): (사망만)슬로우모션+쉐이크→(사망만)빨간 비네트+암전 / (클리어)암전만 → 종료메시지 → `_run.EndRun(isCleared,...)`(메타저장) → `AppBootstrapper.EndRun()`(→`ClearLocalRun` `AppBootstrapper.cs:83` **로컬 세이브 삭제**) → `RequestLoad(BaseCamp)` 허브복귀. `OperationCanceledException` catch + timeScale 복원 보장.

### STEP 9 — 이어하기(결정성) ✅통과
- 진입: `ContinueProcGenRunAsync`(`:2333`) — 로컬세이브 없으면 `StartCombatDirectAsync` 폴백(로그), 있으면 플레이어 스폰→서약/룬보드 복원→`flow.ResumeAsync`.
- 결정성(`RunFlowController.cs`): `ResumeAsync`가 `_masterSeed=meta.masterSeed`(126)+동일 시드로 `RunSequencer` 재구성(141)+`RestoreState(visitCount, seqPhase, shopUsed, eventUsed, cooldowns)`(142). 방 빌드 RNG=`Combine(_masterSeed, VisitCount)`(298) → **동일 (마스터시드, visitCount)면 스포너 플랜까지 완전 재현.** 세이브에 `masterSeed/visitCount/currentRoomPoolKey` 기록(394~401).

---

## 🔴 블로커 (정적 분석 기준: 없음)

정적 코드 추적으로 확정된 실행 차단 블로커 없음. (초기 B 에이전트의 Phase=Running 블로커는 오탐으로 기각 — STEP 3 참조.)

## ⚠️ 조건부 위험 — 우선순위 목록

| 순위 | 항목 | 발생조건 | 영향 | 수정방향 | 근거 |
|---|---|---|---|---|---|
| P1 | 상점 NPC 프리팹 미로드 | `shopNpcAddressableKey` Addressable 미등록/로드실패 | 상점 UI 못 엶(상점방 진행정지 가능) | 키 등록·로드 보장, 실패 시 폴백 상호작용 | `ShopRoomController` npcPrefab null 경고 |
| P1 | 보스 스폰테이블 부재 | `ChapterRegistry` 미할당 + `spawnTable` 미설정 + 풀 `addressableKey` 무효 | 보스 미스폰→챕터 전진 불가 | 챕터별 테이블/키 검증, 로드 실패 로그→재시도 | `BossSpawner.ResolveBossSpawnTable` |
| P2 | NavMesh 빌드 실패 | 방 지오메트리/설정 문제 | 몹 미스폰→거짓 클리어 or 정지 | `BuildMapNavMeshAsync` 실패 시 에러표면화/재빌드 | `RunFlowController`/`GameRunBootstrapper` NavMesh 경로 |
| P2 | 스포너 0마리 | 등급필터 미충족/NavMesh 위치 오류 | 거짓 클리어(게이트 미활성) | 0마리 구제(강등 재롤/게임오버 안내) | `RoomWaveController:231~237` |
| P2 | 상점 등급롤 폴백 강등 | `LuckRollTableSO`/`ShopData` CDN 미로드 | 일반등급 위주 진열(밸런스만) | CDN 로드 확인·폴백 품질 개선 | `UI_ShopPanel` BuildSlots 폴백 |
| P3 | Addressables/Backend 초기화 실패 | CDN/네트워크 불통 | UIRoot 미생성 가능(경고만) | UIRoot 실패 시 씬 Canvas 폴백 | `AppBootstrapper.cs:471~493` |
| P3 | `ItemBuffViewSource` NRE | `EffectManager` 미초기화 시점 폴링 | 버프뷰 예외(저위험) | 소스 등록 전 null 가드 강화 | 버프뷰 소스 |
| P3 | 이어하기 `EnsureChapter` 방어갭 | `ContinueProcGenRunAsync`가 `EnsureChapter` 미호출 — `CurrentChapter`가 미설정(0)인 채 절차 재개될 경우 | `HasNextChapter`/보스 해석 오프바이원 가능 | 재개 직후 `_run.EnsureChapter(ResolveCurrentChapter())` 방어 호출 추가 | `GameRunBootstrapper.cs:2333~2378`(호출 없음) |

> P3 방어갭 주석: `ContinueProcGenRunAsync`는 `_run.IsRunning==true`일 때만 도달(`:256`)하므로 `CurrentChapter`는 이미 상위에서 유효 — 세션 내 전환은 `AdvanceToNextChapter:520`, 콜드부팅 재개는 `RestoreFromSaveAsync:264`가 `save.chapter`로 설정. 실제 미설정(0) 도달 경로는 확인 안 됨(저위험). `EnsureChapter`는 신규 런 흐름에는 이미 배선됨(`GameRunSession.cs:534`).

## 최근 추가분이 흐름을 깨는가 — 결론: **깨지 않음**

- **버프뷰(HUD 버프창)**: 방버프 이벤트 즉시갱신 + 동적소스 폴링, null 가드/옵셔널 체이닝. ✅
- **상점 NPC UI**: 표현만 교체·로직 재사용, 구매/환불/골드/훅 전부 배선. 단 프리팹 로드 의존(P1 ⚠️).
- **HUD 재구성**: `_constructed` 중복가드, 타이밍 무관 바인딩. ✅
- **온보딩(BaseCampOnboardingDirector)**: 퀘스트 이벤트 구동, 배치 시 자동 완성·미배치여도 게이트 자체는 로드아웃 조건으로 별도 동작. ✅
- **EventSystem 부트스트랩**: 모든 씬 UI 클릭 보장. ✅
- **물리(저마찰 material + Continuous 충돌, 벽끼임 수정)**: 최근 커밋 `9362b154a`. 이동/충돌 파라미터 변경으로 방 루프 전이(플랫폼/게이트 진입)를 코드상 막지 않음. 실제 끼임/관통 재현은 인게임 확인 권장. ⚠️확인필요(인게임).

## 확인필요(인게임) 목록

- 씬 배치: BaseCamp 온보딩 트리거/제단/서약픽업, 각 GameScene 카메라·라이트.
- Addressable 등록: `Shop/ShopNpc`, `UI_ShopPanel`, `UI_WeaponForgePopup`, 보스 프리팹 키.
- CDN 데이터 존재/폴백: 상점 LuckTable·가격, 챕터별 보스테이블·룸풀·런구조 CSV.
- Ch2~4 보스 실제 스폰 및 챕터 순환(Ch4 클리어→런클리어).
- 보스 "이중 등장" 실재 여부(코드상 경로 미확인).
- 물리 변경 후 벽 끼임/관통 회귀.

---

## 부록 — 조사 방식/신뢰도

- A/B/C 하위 에이전트 리포트 취합 + 오케스트레이터가 최상위 위험(Phase=Running, `_finalChapter`, 오프바이원, 런종료 세이브삭제, 이어하기 시드정합)을 원본 파일로 직접 재검증.
- D(스텝 7~9) 에이전트는 본 통합 시점 미완이나, 해당 구간은 오케스트레이터 직접 검증으로 대체 커버.
- 모든 판정은 근거 file:line 표기. 실행 의존 항목은 정직하게 "확인필요(인게임)"로 분리.
