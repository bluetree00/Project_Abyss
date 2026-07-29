# Project RelicFairy - Claude 지침

## Karpathy Guidelines

출처: [multica-ai/andrej-karpathy-skills](https://github.com/multica-ai/andrej-karpathy-skills)

Behavioral guidelines to reduce common LLM coding mistakes. Merge with project-specific instructions as needed.

**Tradeoff:** These guidelines bias toward caution over speed. For trivial tasks, use judgment.

### 1. Think Before Coding

**Don't assume. Don't hide confusion. Surface tradeoffs.**

Before implementing:
- State your assumptions explicitly. If uncertain, ask.
- If multiple interpretations exist, present them - don't pick silently.
- If a simpler approach exists, say so. Push back when warranted.
- If something is unclear, stop. Name what's confusing. Ask.

### 2. Simplicity First

**Minimum code that solves the problem. Nothing speculative.**

- No features beyond what was asked.
- No abstractions for single-use code.
- No "flexibility" or "configurability" that wasn't requested.
- No error handling for impossible scenarios.
- If you write 200 lines and it could be 50, rewrite it.

Ask yourself: "Would a senior engineer say this is overcomplicated?" If yes, simplify.

### 3. Surgical Changes

**Touch only what you must. Clean up only your own mess.**

When editing existing code:
- Don't "improve" adjacent code, comments, or formatting.
- Don't refactor things that aren't broken.
- Match existing style, even if you'd do it differently.
- If you notice unrelated dead code, mention it - don't delete it.

When your changes create orphans:
- Remove imports/variables/functions that YOUR changes made unused.
- Don't remove pre-existing dead code unless asked.

The test: Every changed line should trace directly to the user's request.

### 4. Goal-Driven Execution

**Define success criteria. Loop until verified.**

Transform tasks into verifiable goals:
- "Add validation" → "Write tests for invalid inputs, then make them pass"
- "Fix the bug" → "Write a test that reproduces it, then make it pass"
- "Refactor X" → "Ensure tests pass before and after"

For multi-step tasks, state a brief plan:
```
1. [Step] → verify: [check]
2. [Step] → verify: [check]
3. [Step] → verify: [check]
```

Strong success criteria let you loop independently. Weak criteria ("make it work") require constant clarification.

---

**These guidelines are working if:** fewer unnecessary changes in diffs, fewer rewrites due to overcomplication, and clarifying questions come before implementation rather than after mistakes.

---

## 코드 컨벤션 (필수 준수)
- **비동기**: `UniTask` 사용 (코루틴 사용 금지), `CancellationToken` 전달 및 `OperationCanceledException` catch 필수
- **리소스 로드**: `AddressableManager` 경유 (Resources.Load 사용 금지)
- **이벤트**: C# `event Action` 기반 (UnityEvent 지양), `OnEnable`에서 구독 / `OnDisable`에서 해제
- **UI 데이터 흐름**: `Provider → Presenter → View` 3단 구조 준수
- **ScriptableObject**: 정적 데이터(설정값)만 사용, 런타임 상태 저장 금지, `[CreateAssetMenu]` 필수
- **Canvas 기준**: 1920×1080, Scale With Screen Size, Match 0.5
- **씬 조작**: MCP HTTP 호출만 사용 (에디터 스크립트로 씬 수정 금지)

## Unity C# 규칙 (IMPORTANT)
- **직렬화**: `[SerializeField] private` only — Inspector용 public 필드 금지, 외부 접근은 public read-only 프로퍼티
- **컴포넌트 캐싱**: `Awake()`/`Start()`에서 캐싱 — `Update`/`FixedUpdate`/`LateUpdate`에서 `GetComponent`/`FindObjectOfType`/`GameObject.Find` 절대 금지
- **Null 안전**: 컴포넌트 존재 불확실 시 `TryGetComponent<T>()` 사용
- **할당 금지**: Update 루프에서 `new` 힙 할당, 문자열 접합(`+`) 금지
- **.meta 파일**: 직접 생성/수정/삭제 절대 금지 — Unity가 자동 관리
- **스레딩**: 백그라운드 스레드에서 Unity API 호출 금지
- **클래스 멤버 순서**: `Constants` → `Static` → `[SerializeField]` → `Private` → `Properties` → `Lifecycle`(Awake→OnEnable→Start→Update→OnDestroy) → `Public Methods` → `Private Methods` → `Event Handlers`
- **Animator**: 새 상태 추가 시 `writeDefaultValues = false` 필수

## 컴파일 검증 규칙 (필수)
- 모든 .cs 파일 수정 작업이 끝난 후, 반드시 `refresh_unity` → `read_console`로 컴파일 에러 확인
- 에러가 있으면 즉시 수정 후 다시 `refresh_unity` → `read_console` 확인
- 에러가 0이 될 때까지 반복한다
- 매 파일 수정마다가 아니라, 한 질문(작업 단위)이 끝난 시점에 1회 수행한다

## 핵심 시스템 위치

### 부트스트래퍼 — `Assets/RelicFairy/Systems/Bootstrapper/Scripts/`
- `AppBootstrapper.cs` — 앱 시작점 (DDOL)
- `GameRunBootstrapper.cs` — 런(전투) 진입 부트
- `HudBootstrapper.cs` — HUD 초기화
- `UIRootBootstrapper.cs` — UI 루트(@UIRoot) 초기화
- `SteamManager.cs` — Steamworks 초기화

### 매니저 — `Assets/RelicFairy/Systems/Managers/Scripts/`
- `Managers.cs` — 서비스 로케이터 (`Managers.Instance`)
- `AddressableManager.cs` — Addressables 리소스 로딩 (Resources.Load 대체)
- `UIManager.cs` — UI 라이프사이클/데이터
- `InputManager.cs` — 입력
- `SoundManager.cs` — 사운드
- `SceneTransitionManager.cs` — 씬 전환
- `ObjectPoolerManager.cs` — 오브젝트 풀
- `PlayerManager.cs` — 플레이어 인스턴스 관리
- `CharacterDataManager.cs` / `MonsterDataManager.cs` — 캐릭터/몬스터 데이터
- `ChartLoader.cs` — 차트 CSV 로딩
- `AnimationResourceManager.cs` — 애니메이션 리소스

### 게임 런(전투) — `Assets/RelicFairy/Systems/Stage/RunGame/`
- `GameRunSession.cs` — 런 전체 상태 캡슐화
- `PlayerLoadout.cs` / `PlayerRunState.cs` — 플레이어 빌드/런 상태
- `RunItemInventory.cs` / `ItemStack.cs` / `ItemId.cs` — 인벤토리
- `BuffDataManager.cs` / `BuffRoller.cs` / `BuffEntry.cs` — 버프
- `RoomClearController.cs` / `RoomClearGate.cs` / `RoomWaveController.cs` — 룸 클리어 흐름
- `RoomExitTrigger.cs` / `ZoneEntryTrigger.cs` / `ClearRewardTrigger.cs` — 트리거
- `CombatBarrier.cs` — 전투 봉쇄
- `EndRunResult.cs` — 런 종료 결과
- `StatModifier.cs` / `SynergyRecord.cs` / `RunDelta.cs` — 스탯/시너지

### 게임 플로우 — `Assets/RelicFairy/Systems/Stage/GameFlow/`
- `GameFlow.cs` — 전체 상태 머신

### 맵/스테이지 — `Assets/RelicFairy/Systems/Stage/MapGen/`
- `MapBuilder.cs` / `MapDataManager.cs` / `MapDataLoader.cs` — 맵 생성/데이터 로딩
- `ZoneLayoutManager.cs` / `ZoneProgressionService.cs` / `ZoneMapSlot.cs` — 존 레이아웃/진행
- `CorridorBridgeSpawner.cs` / `CorridorStyleSO.cs` — 복도 생성
- `BlockDef.cs` / `BlockPalette.cs` / `TileType.cs` — 블록/타일 정의
- `DecorationCatalogSO.cs` — 데코레이션 카탈로그
- `Entrances/` — 맵 입장 연출 (`DissolveEntrance`, `MapEntranceRegistry`)
- `Token/` — 토큰 파서/핸들러 시스템 (`TokenParser`, `TokenRegistry`, `Handlers/`)

### 챕터 정의 — `Assets/RelicFairy/Systems/Stage/Stage/`
- `ChapterDataSO.cs` / `ChapterLayoutSO.cs` / `ChapterRegistry.cs` — 챕터 데이터

### 시작방(베이스캠프) — `Assets/RelicFairy/Systems/Stage/StartRoom/`
- `WispController.cs` / `WispCameraFollow.cs` — 위습(요정) 조작
- `CharacterDisplayStand.cs` — 캐릭터 선택대 (무기 선택은 `WeaponForgeAltar.cs`로 대체됨)
- `StartRoomPickup.cs` / `StartRoomGate.cs` — 픽업/게이트

### 상점 — `Assets/RelicFairy/Systems/Stage/Shop/`
- `ShopRoomController.cs` / `ShopStallInteraction.cs` — 상점 룸 컨트롤러
- `ShopCatalogSO.cs` / `ShopItemSO.cs` / `ShopPriceTableSO.cs` — 상점 데이터

### 월드 — `Assets/RelicFairy/Systems/Stage/World/`
- `BossRoomController.cs` — 보스룸
- `RoomPlatform.cs` / `RoomOpenSequencer.cs` — 룸 플랫폼/연출
- `BarrierVolume.cs` / `CheckpointZone.cs` — 배리어/체크포인트

### 플레이어 — `Assets/RelicFairy/Characters/Player/Scripts/`
- `PlayerController.cs` — 플레이어 컨트롤러

### 씬 — `Assets/RelicFairy/Scenes/`
- `Logo.unity`, `Lobby.unity`
- `GameScenes/BaseCamp.unity` — 베이스캠프
- `GameScenes/Tutorial.unity`
- `GameScenes/GameScene_Ch1.unity` ~ `GameScene_Ch4.unity` — 챕터별 인게임
- `GameScenes/GameScene_LichTest.unity` — 리치 보스 테스트씬
