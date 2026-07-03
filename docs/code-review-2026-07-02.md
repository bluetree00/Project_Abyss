# RelicFairy 전체 플로우 & 통합 코드리뷰 — 2026-07-02

> **읽기 전용 리뷰.** 이 문서 작성 과정에서 코드/에셋을 **수정·커밋하지 않았습니다**. 문서(.md) 1개만 생성.
> **방법:** 6개 영역을 하위 에이전트로 병렬 정적 조사(파일 리딩·grep) + Unity MCP는 컴파일/콘솔 상태 확인만.
> **근거:** 모든 지적은 `파일:줄` 표기. 정적 리딩만으로 단정 불가한 항목은 **확인 필요**로 분리.
> **정직성 원칙:** 잘 된 부분도 명시. 과장 없이. 메모리 노트와 실제 코드가 어긋난 경우 코드 기준으로 정정.

| 항목 | 값 |
|---|---|
| 브랜치 | `dev/KBG-D` |
| 리뷰 일자 | 2026-07-02 |
| 컴파일 상태 | ✅ **프로젝트 코드 컴파일 에러 0.** 경고만 존재 (거의 전부 서드파티: Oceanis/KriptoFX/Infinity PBR). 프로젝트 코드 경고 1건: `DragonPatternFloorUtils.cs:83` 폐기 API(`FindObjectsOfType`) |
| 조사 범위 | 부팅/절차생성, 전투, 빌드(룬·아이템·유물·서약), 세이브, UI/이동/카메라, 데이터/성능/아키텍처 |

---

## 1. 엔드투엔드 플로우 맵

### 1.1 부팅 (프로세스당 1회, DDOL)
```
AppBootstrapper.AutoCreate()               [RuntimeInitializeOnLoadMethod(BeforeSceneLoad)]  AppBootstrapper.cs:15-23
  └ Awake()  (AppBootstrapper.cs:388-428)  싱글톤 + DontDestroyOnLoad, @RunProgressManager/@BackendGameData 스폰,
                                            Backend.Initialize(), 16:9 강제
  └ Start() async (AppBootstrapper.cs:440-566)  — 실제 초기화 시퀀스
      1) Managers.Instance 확보 (:443)
      2) Backend init (재확인, IsBackendInitialized 가드) (:451)   ※ Awake와 이중 (F/A #3)
      3) AddressableManager.InitAsync() (:475)
      4) Sound + InitSoundTableAsync (SoundEventTable/GameAudioMixer via Addressables) (:478-479)
      5) InitQuestManagerAsync (:482)
      6) EnsureUIRootAsync → @UIRoot 프리팹 인스턴스(DDOL) + Managers.UI.SetRoots (:485-494)
      7) 로그인: useAutoLogin=true → DeviceAutoLoginAsync (deviceUniqueIdentifier) (:518-545)
             성공: Item/Rune/RelicStat 데이터 + BackendGameData.LoadAsync,  실패: Addressables 폴백
      8) startFlow=false(기본) → sceneLoaded += OnSceneLoadedNoFlow + AutoShowUIForCurrentScene (:557-563)
             ※ GameFlow 상태머신은 startFlow=true일 때만 사용 (사실상 휴면, 리뷰 A #7)
      9) IsReady=true (:565)  — 모든 GameScene 부트가 이 플래그를 대기
```
**상태 소유자:** `AppBootstrapper.Instance`(DDOL) — `Loadout`, `CurrentRun`(GameRunSession), `IsNewRunPending`, `IsChapterAdvancePending`.

### 1.2 로비 → 허브(BaseCamp) → 신규 런
```
Lobby "시작" / BaseCamp 게이트
  → AppBootstrapper.RequestStartRun → 시작 영상 → RequestLoad(BaseCamp)  (:171-194)
  → BaseCampBootstrapper.EnterDungeon()  (BaseCampBootstrapper.cs:92-98)
        · AppBootstrapper.MarkNewRunPending()  ·  RequestLoad(GameScene_Ch1)
        · SpawnPlayerAsync에서 Loadout body key 설정 → Loadout.IsReady=true  (:104)
```

### 1.3 씬 전환 메커니즘 (2경로)
- **No-flow(기본):** `RequestLoad → LoadSceneNoFlowAsync` (AppBootstrapper.cs:124-164). 각 씬 부트가 `NotifySceneReady()`로 로딩 오버레이 해제.
- **Flow(startFlow=true일 때만):** `RequestLoad → GameFlow.RequestLoad → SceneTransitionManager.LoadScene` (GameFlow.cs:47-71). 씬→`GameFlowState` 매핑.
- 두 경로가 거의 동일한 async 로드를 **중복 구현**(리뷰 A #7). 씬→상태 맵: `GameFlow._sceneStateMap` (GameFlow.cs:23-33, Ch1..Ch4→InGame).

### 1.4 GameScene 부트 & 런-진입 라우터
```
GameRunBootstrapper.Awake (:160-192)  싱글톤, CurrentRun 채택 or 신규 BeginRun, HUD 바인딩, 이벤트 구독
GameRunBootstrapper.Start (:202-282)  AppBootstrapper.IsReady 대기 → 7개 데이터매니저 init(각 try/catch + CDN→JSON 폴백)
  라우터 분기 (:254-263):
    1) IsRunning && chapterAdvance → StartNextChapterInSceneAsync     (챕터 전환/웨이팅룸)
    2) IsRunning                  → ContinueProcGenRunAsync          (세이브 이어하기)
    3) newRunFromHub              → StartWaitingRoomAsync            (허브발 신규 런)
    4) IsInStartRoom              → StartRoomAsync                   (레거시 캐릭/무기 픽업 시작방)
    5) else                       → StartCombatDirectAsync           (에디터 직접플레이 폴백)
  → NotifySceneReady() (:265)
```

### 1.5 웨이팅룸 → 던전 시작(절차생성)
```
StartWaitingRoomAsync (:2128-2176)  Zone0 클린 웨이팅룸, 카메라 투어,
    · _run.StartNewRunAsync(...) → Phase=Running 설정  (:2155-2157)  ★ 과거 버그 수정 지점
    · 플레이어 스폰 + HUD 바인딩. 던전 빌드는 게이트로 지연
StartRoomGate.ExitStartRoomAsync (StartRoomGate.cs:325-348)  서약 적용 → 해제 → StartProcGenRunAsync → HUD 복원
StartProcGenRunAsync (:761-781)  ResolveCurrentChapter → _run.EnsureChapter(chapter)(오프바이원 가드) →
    풀키/구조키 해석(서버→SO→CHAPTER_N_ROOM_POOL 규칙) → RunFlowController.StartRunAsync(anchor(0,0,2000), poolKey, structureKey)
```

### 1.6 절차생성 방 루프
```
RunFlowController.StartRunAsync (RunFlowController.cs:80-112)
    풀 로드 → 구조 config 해석(Inspector SO → CSV RUN_STRUCTURE → Addressables SO → null=전부 Normal) →
    System.Random(seed) → RunSequencer + RunPlan(결정적 일정표) → 첫 방 진입
RunFlowController.EnterRoomAsync (:278-357)
    화면 와이프 → leapfrog anchor(z 0/300 교대) → roomRng=Random(Combine(masterSeed,visitCount)) →
    GameRunBootstrapper.BuildProcRoomAsync(숨긴 블록/문 빌드) → 플레이어 텔레포트 → 이전 방 스태거 파괴 →
    디졸브 공개 → 게이트 봉인 → RoomWaveController.OnRoomCleared 구독 → SaveRunState (방 경계 저장, :355-356)
HandleRoomCleared (:457-469)  RunSequencer.RollExits() → 게이트 공개 → TransitionAsync → CommitEntry → 다음 EnterRoomAsync
RunSequencer  결정적 출구 로더. Normal→PreBoss→Boss→Done (BossThreshold, :145-151),
    마일스톤 강제(Shop/Event/Elite), 상점/이벤트 캡, 쿨다운, 난이도 창. BuildPlan은 클론에서 재시뮬(드리프트-프리 프리뷰)
```

### 1.7 상점/보스 방
- **상점:** 월드 매대 → NPC+UI 패널로 교체(`ShopNpcInteraction` + `UI_ShopPanel` + `UI_ShopSlotView` + `ShopUIStyle`). 리롤 기본 off(`UI_ShopPanel.cs:194`).
- **보스:** `BossSpawner`가 배치보스(`placedBoss`, m_IsActive=0 시작) 또는 Addressable 스폰. `IBossEntrance` 미구현 보스는 디졸브 등장(`BossSpawner.cs:130`). 보스 클리어 → `OnBossRoomClearedHandler`.

### 1.8 챕터 전환 & 런 종료
```
보스 클리어(비최종) → HasNextChapter() 시 ChapterGate 스폰 (:196-200)
AdvanceChapter (:821-850)  run.EnterChapterClear() → AdvanceToNextChapter();
    false면 HandleRunClear(최종), else MarkChapterAdvance() + RequestLoad(다음 챕터 씬) → 다음 씬이 StartNextChapterInSceneAsync 라우팅
사망: HandlePlayerDeath → HandleRunEndAsync(false)
클리어: HandleRunClear → HandleRunEndAsync(true)  (:857-891)
    슬로모/셰이크 → 페이드 → 메시지 → _run.EndRun(OnRunEnded→메타 저장) →
    AppBootstrapper.EndRun()(ClearLocalRun + Loadout.Clear) → RequestLoad(BaseCamp)
```

### 1.9 세이브/복원
```
저장(진행 중 런, 로컬 3슬롯 권위):
    RunFlowController.EnterRoomAsync 말미 → SaveRunState() (방 경계 1회, :355-356)
    → RunProgressManager.SaveRunLocal → LocalFileRunSaveStore (run_save_{slot}.json)
    → Atomic write: tmp → 기존을 .bak 복사 → 기존 삭제 → tmp→main 이동 → HMAC 사이드카 (:57-70)
복원(이어하기):
    UI_SaveSlotPanel → AppBootstrapper.RequestRestoreRun(slot) → 로컬 로드 →
    GameRunSession.RestoreFromSaveAsync(save) (:255-326, Phase=Running) → BeginRun + RequestLoad(챕터 씬) →
    GameRunBootstrapper.ContinueProcGenRunAsync (:2333-2378): 서약/룬보드 복원 → RunFlowController.ResumeAsync(meta)
    ResumeAsync (:119-161): masterSeed 주입 → 동일 시드 RunSequencer + RestoreState → BuildPlan(일정표 재생성) → 입구에서 시작
```
- **데이터 소스 종합:** 앱/런 데이터 = 뒤끝 CDN + Addressables JSON 오프라인 폴백. 세이브/이어하기 = **로컬 파일 단독 권위**(서버 run-progress CRUD 완전 제거). 정적 config = ScriptableObject + Addressables 폴백 키. 결정성 = masterSeed 저장 + `Combine(masterSeed, visitCount)` 재파생.

---

## 2. 우선순위 Top 이슈 (심각도순)

### 🔴 최우선
| 순위 | 영역 | 이슈 | 근거 | 조치 |
|---|---|---|---|---|
| 1 | 세이브 | **앱 종료/포커스 상실 저장 훅 부재.** `OnApplicationQuit/Pause/Focus` 어디에도 세이브 미연결. 방 경계 자동저장만 존재 → **전투 중 강제종료 시 그 방에서 얻은 골드/아이템/HP 변화 전부 소실, 방 입구부터 재시작.** 크래시는 아니나 진행 되감김. | `SaveSanitizer`/전체 grep, `RunFlowController.cs:355-356`, `Managers.cs:441` 등만 존재 | quit/pause 스냅샷 저장 검토. 단 "방 경계만 저장(사망직전 저장 악용 방지)" 정책과 충돌 → **기획 결정 + QA 확인 필요**. 최소한 "방 도중 종료=방 되감김" 명세화 |
| 2 | 전투 | **DragonBoss가 TimeScaleArbiter 우회.** 히트스톱에서 `Time.timeScale = 0.05f` 직접 설정 후 `finally`에서 `= prev` 복원. 프로젝트 금지 안티패턴. Pause/SlowMotion과 겹치면 `prev`가 잘못된 기준을 잡아 **게임을 언포즈하거나 timeScale이 0.05에 고착** 가능. (LEE 담당 보스 코드) | `DragonBossMonster.cs:544-545, 553` | `TimeScaleArbiter.Acquire(HitStop)/Release` 경유 또는 `HitFeelService.HitStop` 위임. **현재 유일한 실 위반자**(정상 시스템은 전부 Acquire/Release) |

> **참고 — 사전 의심 🔴 2건은 이미 수정됨:** (a) 허브 런 Phase=Running 미설정 → 폴스루/자동저장 스킵 → `GameRunBootstrapper.cs:2155-2157`에서 `StartNewRunAsync` 호출로 **수정 확인**. (b) `ResolveBossSpawnTable` 오프바이원 + 데드 바인딩 → `EnsureChapter` 가드(`GameRunSession.cs:534`) + `chapterRegistry` 직접 참조로 **수정 확인**.

### 🟡 높은 주목도
| 영역 | 이슈 | 근거 |
|---|---|---|
| 리소스 | **Addressable 에셋 회수 경로 데드.** `ReleaseAll/ReleaseAllAssets` 호출자 0, 스코프 언로드(`LoadAssetAsync(key,scope)` + `ReleaseScope`)도 호출자 0 + `enableScopedAssetUnload=false`로 완전 휴면. 로드된 프리팹/VFX/SO가 앱 수명 내내 상주 → 4챕터 런이 모든 테마 에셋 동시 보유(bounded지만 세션 간 회수 없음) | `AddressableManager.cs:189-202, 334-376`, `AppBootstrapper.cs:94-101` |
| 데이터 | **MonsterDataManager 컬처 민감 float 파싱.** move_speed/attack_range/cooldown을 `CultureInfo.InvariantCulture` 없이 파싱 → 콤마 소수점 로케일에서 "1.5"→15/0 손상. 유일하게 공용 `TryGetFloat` 헬퍼 미사용 | `MonsterDataManager.cs:92-94` |
| 성능 | **MapBuilder 동기 블록 인스턴스화.** Build/Ceiling/Lights 사이에만 yield, 내부엔 없음 → 큰 방이 한 프레임에 빌드되어 히치. 존맵 경로는 yield 전무 | `MapBuilder.cs:41-191`, `GameRunBootstrapper.cs:543` |
| 성능 | **풀러 spawn/despawn마다 `GetComponent<IPooledObject>()`(인터페이스 오버로드).** 투사체/이펙트 다발 프레임에서 측정 가능한 GC | `ObjectPoolerManager.cs:144, 272` |
| 성능/전투 | **에임어시스트 `Physics.OverlapSphere`(배열 할당) + 콜라이더별 `GetComponent`.** 매 프레임은 아니나 콤보 전투 핫패스라 지속 GC | `PlayerController.cs:1498, 1505` |
| 세이브 | **HMAC 경고 전용 — 변조 세이브 그대로 로드.** 불일치여도 로드 진행, 키는 빌드 내장 평문. 의도된 데이터손실0 정책 + 로드시 클램프가 보완하나, 랭킹을 로컬 통계에 의존 시 부풀린 값 서버 미러 우려 | `SaveIntegrity.cs:36`, `LocalFileRunSaveStore.cs:169-172` |
| 빌드 | **무동작 서약 2종이 라이브 랜덤 풀에 포함.** `Leodegrance`/`Guinevere`가 명시적 no-op 스텁인데 `WorldCovenantPickup`이 `AllIds` 12종 전부 롤 → 아무것도 안 하는 트랩 선택지 | `LeodegranceCovenant.cs:8`, `GuinevereCovenant.cs`, `WorldCovenantPickup.cs:96,111` |
| 전투 | **몬스터 공격 2경로 결과 상이.** 기본 `AttackState→attackShape.Execute` vs 패턴 `TakeDamage 직접+슬로우+넉백`. 슬로우/넉백이 패턴 경로에만 적용 → 피격 체감/텔레그래프 정합 불일치 | `PatternAttackOverrideSO.cs:250,324` vs `AttackState.cs:66` |
| 부팅 | **씬 로드 2중 구현 + GameFlow 휴면.** `LoadSceneNoFlowAsync`가 `SceneTransitionManager.LoadSceneAsync`를 거의 그대로 복제. startFlow=false 기본 → GameFlow 상태머신 사실상 데드. 분기 발산 위험 | `GameFlow.cs`, `AppBootstrapper.cs:124-164` |
| 부팅 | **HudBootstrapper.LateUpdate에서 Find.** ~0.2s마다 전체 `MonsterBase` 스캔 + `Resources.FindObjectsOfTypeAll<Transform>`. Update 금지 규칙 위반 + 무거움 | `HudBootstrapper.cs:249, 371-380` |
| 세이브 | **메타 멀티기기 충돌.** 로컬 존재 시 무조건 로컬 적용(세대/타임스탬프 비교 없음) → 다른 기기 최신 서버값 덮임. SteamCloud 미착수라 현재 노출 낮음, 출시 전 LWW 필요 | `BackendGameData.LoadAsync:95-101` |

---

## 3. 영역별 상세 리뷰 (심각도표)

### 3.A 부팅 / 씬 전환 / 앱 수명주기 / 절차생성
| # | Sev | 근거 | 내용 | 수정 방향 |
|---|-----|------|------|-----------|
| A1 | 🟢(해결) | `GameRunBootstrapper.cs:2155-2157` | Phase=Running 폴스루 버그 **수정됨**(StartNewRunAsync 호출, 자동저장 정상) | 없음 |
| A2 | 🟢(해결) | `GameRunSession.cs:534-539`, `GameRunBootstrapper.cs:770` | ResolveBossSpawnTable 오프바이원 **수정됨**(EnsureChapter 가드 + registry 직접 참조) | 없음 |
| A3 | 🟡 | `AppBootstrapper.cs:413-425 vs 451-463` | Backend가 Awake·Start 이중 초기화(둘 다 가드) — Start쪽 사실상 데드 | 한 곳으로 단일화 |
| A4 | 🟡 | `AppBootstrapper.cs:648-653` | `CustomSignUp`이 메인스레드 동기 블로킹(CustomLogin은 콜백 TCS) → 부팅 스톨 우려 | 콜백/TCS 비동기화. **확인 필요**: SDK가 실제 블로킹하는지 |
| A5 | 🟡 | `HudBootstrapper.cs:249,371-380`, `GameRunBootstrapper.cs:243,268-280` | LateUpdate 보스 스캔 + `Resources.FindObjectsOfTypeAll` (무거움, Update 금지 위반) | Panel_Combat 참조 캐싱, 보스 이벤트/레지스트리 등록 |
| A6 | 🟡 | `AppBootstrapper.cs:440` `async void Start` | 예외 escape + destroy 토큰 없음(DDOL이라 실위험 낮음) | `GetCancellationTokenOnDestroy` 체인 |
| A7 | 🟡 | `GameFlow.cs` + `AppBootstrapper.cs:548-563` | startFlow=false 기본 → GameFlow 휴면, 씬 로드 2중 구현 발산 위험 | 한 경로로 통합 |
| A8 | 🟡 | `RunSequencer.cs:60,244-246` | `DifficultyTolerance=0.35f` 하드코딩(나머지는 CSV/SO 데이터 주도) | 밸런스 반복 예상 시 `IRunStructure`로 이동 |
| A9 | 🟡 | `AppBootstrapper.cs:509-513,525-530` | Item/Rune/RelicStat init을 `UniTask.WhenAll` — 개별 try/catch 없음, 하나 throw 시 배치 전체 중단(GameRunBootstrapper는 개별 가드) | 개별 try/catch 래핑. **확인 필요**: InitializeAsync 내부 예외 삼킴 여부 |
| A10 | 🟡 | `GameRunBootstrapper.cs:2372,763` | `runFlowController ?? AddComponent<RunFlowController>()` 3곳 반복 — 씬 내 재진입 시 컴포넌트 스택 우려(각 경로 one-shot이라 저위험) | Awake에서 find-or-create 1회로 가드 |
| A11 | 🟢 | `RunFlowController.cs:252-266` | 임시 검증 로그(`[RunStructure검증]`, DumpRunPlan) 잔존(자체 표기) | 출시 전 제거 |
| A12 | 🟢 | `GameRunBootstrapper.cs:284-317` | OnDestroy 정리 우수(무기슬롯 저장, 맵 인스턴스 릴리즈, 이벤트 해제) | 없음 — 잘 됨 |

### 3.B 전투 (데미지·판정·크리·히트스톱·FSM)
| # | Sev | 근거 | 내용 | 수정 방향 |
|---|-----|------|------|-----------|
| B1 | 🔴 | `DragonBossMonster.cs:544-545,553` | TimeScaleArbiter 우회(직접 timeScale set) — Top이슈 #2 | Acquire/Release 또는 HitFeelService 위임 |
| B2 | 🟡 | `PatternAttackOverrideSO.cs:250,324` vs `AttackState.cs:66` | 몬스터 공격 2경로 결과 상이(슬로우/넉백 편차) | 공용 `ApplyPlayerHit(dmg,kb,slow)`로 funnel |
| B3 | 🟡 | `PatternAttackOverrideSO.cs:367,292,481,118` | 패턴 공격 핫패스 GC(`new Vector2Int[]`/Instantiate VFX/`new Material`/`new List`) | 그리드셀 static readonly, VFX 풀링, 머티리얼 재사용, 후보 리스트 프리얼록 |
| B4 | 🟢(검증) | `IBossEntrance`, `Arena_Boss_Ch1.prefab:469-470`, `BossSpawner.cs:130-143` | Ch1 보스 이중등장 **수정 확인**(FG는 IBossEntrance 미구현, 인스턴스 m_IsActive=0, "이중 등장 방지" 가드 존재) | 없음. **메모리 `project_ch1_boss_double_appear` stale → resolved로 갱신 권장** |
| B5 | 🟡 | `MonsterBase.cs:973,982` | 보스 바인드/언바인드에서 `FindAnyObjectByType<HudPresenter>`(프레임 아님, 라이프사이클당 2회) | HudPresenter 참조 부트에서 캐싱 |
| B6 | 🟢 | `DieState.cs:41,53` | 사망 시 rb/collider 재스캔(MonsterBase 캐시 미재사용) | 캐시된 콜라이더 재사용(웨이브 다발 시 스파이크 저감) |

**검증된 사항(문서와 일치):** ApplyDamageTakenAmp 다중슬롯(statusId별 사전, 동일 id overwrite/상이 id 합산, 단일소스 회귀0, 무할당 열거 — `MonsterBase.cs:613-632`) · 피격 비네트 OnDamageTaken 경유(몬스터 직접 TakeDamage → 이벤트 → HitFxPresenter) · TimeScaleArbiter 우선순위 Pause>SlowMotion>HitStop · 크리/방어 레이어링(크리는 플레이어측 계산·isCrit 플래그만 전달, 몬스터측 재승산 없음) · 플레이어 3레이어 가산 데미지(CDN base_attack) · 에임어시스트 7m 하한 · 룸클리어 흐름.

### 3.C 빌드 시스템 (룬·아이템·유물·서약)
> **완성도 실측:** 룬 시너지 24/24 실구현 · 아이템 ~90 effect_type 전부 실클래스(T3/T4 포함) · 유물 가웨인+랜슬롯 실동작 · 서약 12클래스(10 실동작 + 2 무동작 스텁) · 방버프 실활성 · DamageTakenAmp 다중슬롯 정상. **스텁을 완성으로 위장한 사례 없음.**

| # | Sev | 근거 | 내용 | 수정 방향 |
|---|-----|------|------|-----------|
| C1 | 🟡 | `LeodegranceCovenant.cs:8`, `GuinevereCovenant.cs`, `WorldCovenantPickup.cs:96,111` | 무동작 서약 2종이 라이브 롤 풀에 포함(트랩 선택지) | 구현 또는 `IsPlayable` 플래그로 롤 제외 |
| C2 | 🟡 | `Editor/CovenantDataGenerator.cs:23-44` | 구버전 4종만 생성하는 stale 에디터 제너레이터(v2 12종 오도). 런타임 무해(에디터 전용) | 삭제 또는 12종 재작성 |
| C3 | 🟡 | `RuneEffectFactory.cs:4-6` | 클래스 doc 주석이 "전기 4종만 실효과, 20종 빈 폴백"이라 실제(24종 전부 구현)와 모순 | 주석을 실제에 맞게 갱신 |
| C4 | 🟢 | `RunItemInventory.cs:166-171` | `ResolveMaxStack` 데드코드(PlaceItem이 호출 안 함, maxStack 미강제) | 강제 필요 여부 확정 후 배선 or 제거 |
| C5 | 🟢 | `ItemStack.cs`, `ItemId.cs` | "(예시)" 주석이나 실제 사용(`GameRunSession.cs:684` 등). string itemId와 int ItemId 이중 식별 체계 | 주석 정정, 식별 체계 통일 검토 |
| C6 | 🟢 | `CharacterData.cs` | 전 필드 public(컨벤션 위반). 레거시 데이터 SO, 광범위 참조 | 전용 리팩터 아니면 유지(캐주얼 수정 금지) |
| C7 | 🟢 | `MonsterStatusReceiver.cs:183-190` | DoT 루프가 킬 후에도 remainingTicks 계속(무해 — TakeSynergyDamage가 IsDead 조기반환) | 명료성 위해 `if(IsDead) break` |

**강점:** 룬 tier-drop `OnDeactivate` 역적용(가장 어려운 정합면) 정확 · 틱 집계 struct 기반 무할당(`ItemDynamicStats`/`ItemCombatModifiers`/`RuneResourceState.Tick`) · 이벤트 구독 위생(Dispatcher.Detach, CovenantHandler.Cleanup, ItemEffectManager.Cleanup) · 다중소스 가산 채널 무충돌.

### 3.D 세이브 / 복원 / 무결성 / 텔레메트리
| # | Sev | 근거 | 내용 | 수정 방향 |
|---|-----|------|------|-----------|
| D5 | 🔴 | quit/pause grep | exit-save 훅 부재(Top이슈 #1) — 방 도중 종료 시 진행 소실 | 기획 결정 + QA. quit/pause 스냅샷 검토 |
| D1 | 🟡 | `SaveIntegrity.cs:36`, `LocalFileRunSaveStore.cs:169-172` | HMAC 경고 전용 + 키 평문 → 변조 로드 가능(의도 정책, 클램프 보완) | 랭킹 지표는 서버 재계산, HMAC은 탐지용 유지 |
| D3 | 🟡 | `SaveSanitizer.cs:57`, `RunFlowController.cs:97` | masterSeed 미클램프 + `Environment.TickCount` 시드. 텔레메트리에 seed 미전송 | 텔레메트리에 masterSeed 포함(재현) |
| D4 | 🟡 | `RunSaveData.cs`(position 없음), `ResumeAsync:156` | 위치 미저장 → 항상 방 입구 재시작(설계 의도). **방 클리어~다음방 진입 전 종료 시 보상 중복 획득 가능성 확인 필요** | 저장 경계 재확인 |
| D8 | 🟡 | `GameRunSession.RestoreFromSaveAsync:280-307` | 복원 시 synergies append와 룬 재계산 순서 의존(new 세션이라 1회성이면 안전) | **확인 필요**: RefreshAwakening/RestoreSynergies 이중 적용 여부 |
| D9 | 🟡 | `BackendGameData.SaveAsync:136-162` | 텔레메트리 = 누적통계뿐(seed/사망원인/방번호 없음), 실패 시 LogError만(재시도 없음). fire-and-forget 미완(설계 PR5) | 상세 스키마 후속 |
| D10 | 🟡 | `BackendGameData.LoadAsync:95-101` | 멀티기기 충돌(LWW 없음). SteamCloud 미착수라 노출 낮음 | 출시 전 timestamp/generation 비교 |
| D7 | 🟡 | `LocalFileRunSaveStore` | 파일 I/O 전부 동기(`File.WriteAllText`), UniTask/CT 미사용(비동기 규칙 위반). 데이터 작아 히치 경미 | 대용량화 시 `RunOnThreadPool` |
| D2 | 🟢 | `SaveSanitizer.cs:32-73` | 로드시 값 클램프 견고(음수/범위/HP/chapter/weaponSlot, NaN·Inf 가드, 파싱실패 폴백) | 없음 — 잘 됨 |
| D6 | 🟢 | `LocalFileRunSaveStore.Save:51-76` | Atomic write 견고(tmp→bak→move, 예외 격리, .bak 폴백) | 없음 — 잘 됨 |

**SteamCloud:** RemoteStorage/FileWrite/FileRead grep **0 매치 — 미착수 확인.** 서버 run-progress CRUD 완전 제거(로컬 단독 권위 정합). 결정성: masterSeed+visitCount+시퀀서 상태 저장 → 방 토폴로지/출구 재현(상점/보상/버프 롤은 post-entry라 미재현, 설계상 의도).

### 3.E UI/HUD + 이동/회피/카메라
| # | Sev | 근거 | 내용 | 수정 방향 |
|---|-----|------|------|-----------|
| M1 | 🔴 | `PlayerController.cs:1498,1505` | 에임어시스트 `Physics.OverlapSphere`(할당) + 콜라이더별 GetComponent, 콤보 핫패스 지속 GC | `OverlapSphereNonAlloc` + LayerMask(`CameraOcclusionFader.cs:135` 패턴 참고) |
| C2 | 🟡 | `CameraOcclusionFader.cs:24` | 코드 기본 `occlusionMask=~0`(Everything)이 메모리 "inert/Nothing" 노트와 **모순** → inert는 씬 직렬화값에서 옴 | **확인 필요**: 씬/프리팹 YAML의 직렬화 마스크 |
| U3 | 🟡 | `CombatPanelView.cs:877-878` | `SetStats`가 이모지 문자열 보간(`$"⚔ {atk}"`) 매 스탯 이벤트마다 → GC | 접두어 캐싱/변경 시에만 리빌드 |
| U5 | 🟢 | `CombatPanelView.cs:457` | 폴백 폰트 `Resources.Load`(AddressableManager 경유 규칙 위반) | Addressable/직렬화 폰트 참조 |
| M2 | 🟡 | `PlayerController.cs:1057,1567` | 공격 클릭/마우스룩마다 `Camera.main`(내부 FindGameObjectsWithTag) | 게임플레이 카메라 1회 캐싱 |
| C1 | 🟡 | `GameCameraController.cs:96,158,...` | `FindFirstObjectByType<CinemachineFreeLook>` ~8곳 재해석(Update 아님, vcam 수명 우려 스멜) | Awake 1회 해석. **확인 필요**: vcam 파괴/재생성 여부 |
| U1 | 🟡 | `CombatPanelView.cs:16-1179` | 뷰가 대부분 코드로 자기 구성(런타임 `new GameObject`+이름 문자열 결합) — 프리팹 배선 우회 | 장기적으로 프리팹 authoring + `[SerializeField]`, Ensure는 폴백만 |
| U2 | 🟡 | `HudPresenter.cs:69-78` | 0.25s 폴링 시 `_buffAggregator.Collect()`가 매번 List 할당 가능성 | **확인 필요**: Collect 버퍼 재사용 여부 |
| U6 | 🟢 | `UI_ShopPanel.cs:46-50` | UI 토글이 레거시 Input(플레이어는 신규 Input System) 혼용 | InputManager/신규 경유 |
| C3 | 🟢 | `CameraOcclusionFader.cs:191-220` | 오클루더별 `new Material`(정상 Destroy되나 스윕 시 GC 스파이크) | 소스셰이더 키 공유 페이드 머티리얼 풀 |
| C5 | 🟢 | `GameCameraController.cs:388-417,450-502` | Dragon 탑다운 async lerp 버전 데드코드(스냅 버전으로 대체됨) | 언급만(삭제 금지 규칙) |
| M4 | 🟢 | `PlayerController.cs:690-731` | Update가 `cinemachineCamera==null`이면 전체 로직 정지(카메라 결손이 "플레이어 멈춤"으로 위장) | 비카메라 로직 분리 or 1회 로그 |

**강점:** `EventSystemBootstrapper`가 중복/결손 EventSystem 위험의 정확한 해결(단일 가드, 씬 바운드) · `DodgePresentation`은 모범(캐싱, OnEnable/OnDisable 대칭, 인스턴스 풀, UniTask+CT+OCE catch, 무할당) · facing FixedUpdate slew 프레임독립 설계 · Effect 표시 레이어(EffectMetaRegistry+Formatter)는 비침습 단일 진입점 · 버프창 5소스 집계 설계 일치 · Provider→Presenter→View 3단 준수.

### 3.F 데이터 파이프라인 / 성능·GC / 리소스 수명 / 아키텍처
| # | Sev | 근거 | 내용 | 수정 방향 |
|---|-----|------|------|-----------|
| R1 | 🟡 | `AddressableManager.cs:334-376`, `AppBootstrapper.cs:94-101` | `ReleaseAll*` 호출자 0 → 에셋 앱 수명 내내 상주, 세션 간 회수 없음 | 로비 복귀/챕터 전환 시 회수 |
| R2 | 🟡 | `AddressableManager.cs:189-202` | 스코프 로드 오버로드 호출자 0 → `ReleaseScope` no-op, `enableScopedAssetUnload=false` (PR6 경로 완전 휴면) | 실제 스코프 로드 배선 후 활성 or 미완 항목으로 추적 |
| D1 | 🟡 | `MonsterDataManager.cs:92-94` | 컬처 민감 float 파싱(유일하게 InvariantCulture 헬퍼 미사용) | `row.TryGetFloat` 경유 |
| P1 | 🟡 | `MapBuilder.cs:41-191`, `GameRunBootstrapper.cs:543` | 동기 블록 인스턴스화 → 큰 방 한 프레임 히치, 존맵 경로 yield 전무 | 내부 x 루프 청킹 + 주기적 yield |
| P3 | 🟡 | `ObjectPoolerManager.cs:144,272` | spawn/despawn마다 인터페이스 GetComponent | CreateInstance 시 참조 캐싱 |
| P2 | 🟡 | `DragonPatternFloorUtils.cs:83` | 폐기 `FindObjectsOfType<BoxCollider>()` 전체 스캔(보스전 패턴마다) | 스폰 시 Bounds 1회 캐싱, `FindObjectsByType(...None)` |
| D3 | 🟡 | `ItemDataManager.cs:140-145` | CDN 성공 시 로컬 캐시 통째 교체(버전 게이팅 없음, Monster/RunStructure와 상이) | 부분응답 가능 시 min-row/버전 가드. **확인 필요**: CDN 완전성 보장 |
| D2 | 🟡 | `ChartLoader.cs:104-149` | static CDN 캐시 + `ClearCache()` 호출자 0(버전/TTL 없음) — 출시 전 정책상 의도 | 출시 전 갱신 경계에서 ClearCache |
| P5 | 🟢 | 전역 | data manager/ChartLoader의 raw `Debug.Log`는 릴리즈 미스트립(init/전환 시라 GC 낮음) | `RFLog.D` 선호 |
| A1 | 🟢 | 전역 | `.asmdef` 없음 → 순환참조 위험 없음(대신 모놀리식 Assembly-CSharp, 전체 재컴파일) | 결합/빌드시간 커지면 분할 |
| A5(F) | 🟢 | `MonsterDataManager.cs:9` | 구(JSON)/신(SO via ChartLoader) 몬스터 데이터 경로 공존(TODO 명시) | 편의 시 통일 |
| A3(F) | 🟢 | git status | 레거시 셰이더(MonsterRim/Silhouette) 삭제, C# 참조 0 | **확인 필요**: 씬/머티리얼 GUID 잔존 여부(Unity refresh 시 핑크 확인) |

**강점:** 일관된 3-tier CDN→JSON→Addressables 폴백(런 미차단) · 공용 `JsonData` 헬퍼·RunStructure 패킹의 정확한 InvariantCulture · 데미지팝업/VFX 클린 풀링(무할당 LateUpdate) · 인스턴스 핸들 릴리즈 규율(IsTrackedInstance 분기로 이중릴리즈 방지) · `RFLog`/`MapBuilder.Name` `[Conditional]` 스트립 · 게임플레이 Update 루프에 Find/GetComponent 없음.

---

## 4. 잘 된 점 (정직한 긍정)
- **절차생성 결정성/이어하기**가 이 코드베이스의 최강점: masterSeed + 자식시드 재파생, `BuildPlan` 클론 재시뮬 드리프트 검증, 전반적 취소(CancellationToken/OCE) 처리 — 비동기 규칙 준수.
- **세이브 원자성**(tmp→bak→move) + **로드시 값 클램프/NaN 가드**가 견고. 손상 시 .bak 폴백.
- **빌드 시스템(룬/아이템/유물/서약)이 실제로 완성**되어 있음. 메모리 노트의 "완성" 주장들이 코드와 일치(스텁 위장 없음). tier-drop 역적용·struct 무할당 집계가 특히 정교.
- **DamageTakenAmp 다중슬롯**이 룬/유물/아이템 공유 채널로 깔끔(단일소스 회귀 0).
- **EventSystemBootstrapper**·**DodgePresentation**이 컨벤션 모범 사례.
- **GameRunBootstrapper 매니저별 init**이 개별 try/catch + CDN→오프라인 폴백으로 백엔드 장애에 강함.
- 사전 의심 🔴 2건(Phase=Running, ResolveBossSpawnTable)이 이미 수정+주석 문서화됨.

---

## 5. 확인 필요 (정적 리딩 한계)
- **A4** `CustomSignUp`이 실제 블로킹인지(SDK 내부).
- **A9** Item/Rune/RelicStat `InitializeAsync`가 WhenAll 밖으로 throw 가능한지.
- **A7** 실제 출시 씬의 `AppBootstrapper.startFlow`가 true로 직렬화됐는지(프리팹/씬 미확인, 코드 기본 false).
- **B(전투)** HitFxPresenter 구독/해제 누수 · 몬스터별 attackShape ↔ 그리드 경고 shape 정합(데이터 의존) · ForestGuardian 인게임 등장 스모크 테스트.
- **C(빌드)** 무동작 서약 2종이 현 마일스톤에서 의도적 제공인지(기획 결정) · CDN에 등록된 모든 effect_type의 실제 차트 행 존재 여부(룬은 알려진 키 부재 시 무경고 빈 폴백).
- **D4/D8** 방 클리어~다음방 진입 전 종료 시 보상 중복 가능성 · 복원 시 synergies/룬 이중 적용.
- **E** `BuffViewAggregator.Collect()` 할당 + 유물-첫셀 순서(U2/U8) · 오클루전 씬 마스크(C2) · vcam 수명(C1) · Dragon 탑다운 async 데드코드(C5).
- **F/A3** 삭제된 셰이더 GUID를 참조하는 씬/머티리얼 잔존 여부(Unity refresh 시 핑크 확인).

---

## 6. 권장 조치 순서
1. **[🔴 기획결정]** D5 exit-save 정책 결정 — quit/pause 스냅샷 vs 방경계-only 명세화 + QA.
2. **[🔴 코드]** B1 DragonBoss 히트스톱을 TimeScaleArbiter 경유로(LEE와 협의, 보스 코드).
3. **[🟡 데이터 무결성]** D1 MonsterDataManager InvariantCulture 파싱(로케일 버그, 저비용 고효과).
4. **[🟡 성능]** P3 풀러 GetComponent 캐싱 → P1 MapBuilder 청킹 → M1 OverlapSphereNonAlloc → P2 보스 arena Bounds 캐싱.
5. **[🟡 UX/기획]** C1 무동작 서약 2종 롤 풀 제외(트랩 선택 제거).
6. **[🟡 메모리]** R1/R2 Addressable 회수 — 최소 로비 복귀 시 `ReleaseAll` 호출.
7. **[🟡 위생]** A3/A7 부팅 이중 init·씬 로드 2중 구현 정리, C2/C3/RuneEffectFactory 주석 정정, 임시 검증 로그 제거.
8. **[출시 전]** D10 메타 LWW, D9 상세 텔레메트리, D1(세이브) 랭킹 서버 재계산, ChartLoader ClearCache 경계.
9. **[메모리 갱신]** `project_ch1_boss_double_appear`(→resolved), `project_run_phase_not_running_hub_bug`(→fixed 반영).

---
*생성: 2026-07-02 · 읽기 전용 리뷰 · 코드/에셋 미수정.*
