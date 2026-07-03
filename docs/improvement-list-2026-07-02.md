# RelicFairy 개선 항목 정밀 재검증 리스트 — 2026-07-02

> **읽기 전용 재검증.** `docs/code-review-2026-07-02.md`의 모든 개선 항목을, 인용된 소스 파일을 **직접 열어 정독**해 재확인했습니다. 코드/에셋 수정·커밋 없음(이 .md만 생성).
> **방법:** 6개 영역(A~F)을 하위 에이전트로 병렬 정독 + 각 항목 file:line·심각도·기전 검증. 🔴 2건은 오케스트레이터가 직접 소스 교차확인.
> **컴파일 상태:** ✅ Unity(Project_Abyss@8219311b0edeefd1, 6000.3.10f1) 콘솔 에러 0.
> **정직성:** 리뷰 주장이 틀리면 정정. 정적 한계로 단정 불가한 건 "확인필요"로 명시.

각 항목 형식: **[제목+심각도 / file:line / 현상 / 왜 문제 / 확실성 / 수정방향+난이도(S/M/L) / 선행·의존]**

---

## 재검증으로 바뀐 것 (원 리뷰 대비 정정)

| 항목 | 원 심각도 | 재검증 | 정정 내용 |
|---|---|---|---|
| **M1** 에임어시스트 OverlapSphere | 🔴 | **🟡 하향** | 프레임당이 아니라 **공격 스윙당 1회**. "콤보 핫패스 지속 GC"는 과장 |
| **U2** BuffAggregator.Collect List 할당 | 🟡 | **기각(🟢)** | `_scratch` 버퍼 **재사용**, List 할당 없음. 리뷰 주장 오류 |
| **A5** LateUpdate Find | 🟡 | 🟡(정밀화) | GameRunBootstrapper ~243/268-280은 **LateUpdate 아님**(부팅 1회). **HudBootstrapper 단독** + 0.2초 스로틀 있음 |
| **A10** runFlowController AddComponent | 🟡 | 🟡(정정) | "3곳" → **2곳**(763, 2372). 세 번째 경로는 763 재사용 |
| **P1** MapBuilder 동기 빌드 | 🟡 | 🟡(정정) | 존맵 경로는 **4존마다 yield 존재**. "yield 전무"는 과장(단일 대형 방 내부는 여전히 통째 동기) |
| **P2** 폐기 FindObjectsOfType | 🟡 | 🟡(빈도하향) | "패턴마다" → **보스 스폰/활성 시 1~수회** |
| **B5** 보스 HUD Find | 🟡 | **🟢 하향** | 973/982 Find는 **전부 lifecycle**(per-frame 아님). 실피해 낮음 |
| **A3F** 삭제 셰이더 GUID 잔존 | 🟢(확인필요) | **✅ 해소** | 삭제 아님·`_Legacy/Graphics/`로 **GUID 보존 이동**. 씬/프리팹/렌더러 참조 0 → **핑크 위험 없음** |

**재검증 후 실질 🔴 = 2건: D5(종료 저장 훅 부재), B1(DragonBoss timeScale 우회).** M1은 🟡로 하향.

---

## 🔴 최우선

### D5 — 앱 종료/포커스 상실 저장 훅 부재
- **위치:** `RunFlowController.cs:355-356`(유일한 방경계 저장 `SaveRunState`) → `RunFlowController.SaveRunState:382-408` → `RunProgressManager.SaveRunLocal:78-90`. app-lifecycle 훅 전수: `Managers.cs:441`(`s_isQuitting=true`만), `GuidelineVisualRunner.cs:108`(플래그만), `TheBackend/SendQueueMgr.cs:48/62`(뒤끝 큐 정지, 런 저장 무관).
- **현상:** `OnApplicationQuit/Pause/Focus` 어디에도 런 세이브 미연결. 저장은 방 **진입** 순간(`EnterRoomAsync` 말미) 1회뿐.
- **왜 문제:** 방 안에서 강제종료(Alt+F4/크래시/모바일 백그라운드) 시 그 방 진입 이후 획득분(골드/아이템/HP)이 소실, 이어하기는 항상 방 입구부터. **트리거: 항상**(방 안에서 종료 시). 크래시는 아니고 진행 되감김.
- **확실성:** 확정(훅 부재는 명백). 단 하데스식 "방 단위 저장"이 의도된 손실일 수 있음.
- **수정방향:** `OnApplicationPause(true)`/`OnApplicationQuit`에서 현재 방 상태 스냅샷 저장. 단 `SaveRunState`가 RunFlowController의 masterSeed/sequencer 스냅샷(meta)을 필요로 해 "마지막 meta 캐시 후 훅에서 재저장" 구조 필요. **난이도 M, 회귀 위험 中**(종료 시점 저장이 D4 중복보상 창을 키울 수 있음).
- **선행/의존:** **기획 결정 선행 필수** — "방 단위 손실 허용(현행)" vs "종료 시점 저장". **D4와 한 묶음**("저장 경계 정책")으로 결정. 최소한 "방 도중 종료=방 되감김" 명세화.

### B1 — DragonBoss 히트스톱이 TimeScaleArbiter 우회
- **위치:** `DragonBossMonster.cs:544-545`(`float prev=Time.timeScale; Time.timeScale=0.05f;`), `:553`(finally `Time.timeScale=prev;`). 진입 `:536`. 정당 소유자 `TimeScaleArbiter.cs`(59/77/85). **전수조사 결과 프로젝트 코드 중 직접 대입은 아비터 외 DragonBoss가 유일**(나머지는 `_Imported/EffectSource/...` 써드파티 데모).
- **현상:** `HitStopAsync`가 아비터(Acquire/Release, 우선순위 Pause>SlowMotion>HitStop)를 우회해 `Time.timeScale`을 직접 캡처/대입/복원.
- **왜 문제:** `prev`를 현재 `Time.timeScale`에서 캡처하므로 다른 시스템과 충돌 — (a) 히트스톱 중 Pause 걸리면 finally가 `prev(0.05)`로 복원 → **일시정지 메뉴 떠도 0.05배속으로 흐름**; (b) 진입 시 Pause(0) 상태였으면 `prev=0` 캡처 → finally에서 **0 고착(게임 멈춤)**; (c) 아비터가 `Recompute()` 돌리면 등록 안 된 0.05가 무시돼 히트스톱 조기 종료. **트리거: 특정**(히트스톱 ↔ Pause/SlowMotion/킬 히트스톱 시간 겹침). 아비터 주석(9-11)이 지목한 바로 그 레거시 안티패턴.
- **확실성:** 안티패턴 위반은 확정, 실발생은 **위험 있음**(동시성 타이밍 의존).
- **수정방향:** 직접 대입 3줄을 `TimeScaleArbiter.Acquire(this,0.05f,Priority.HitStop)` / `finally Release(this)`로 교체. **난이도 S, 회귀 위험 낮음**(HitStop 최하 우선순위라 Pause/슬로모에 양보가 의도된 동작). 대안: 다른 보스가 쓰는 `HitFeelService` 경유로 통일.
- **선행/의존:** `HitFeelService.KillImpact`가 아비터 경유인지 1회 확인 후 재사용 권장. **LEE 담당 보스 코드 — 협의 필요.**

---

## 🟡 높은 주목도

### 전투

**B2 — 몬스터 공격 2경로 슬로우/넉백 편차**
- 위치: 패턴 경로 `PatternAttackOverrideSO.cs:324-329`(그리드 히트 TakeDamage+ApplySlow+ApplyKnockback), `:250-256`(원거리 폴백 동일) vs 기본 `AttackState.cs:66`(`DealDamageToPlayer()` 단일).
- 현상: 슬로우/넉백이 패턴 자체 코드에만. 기본 AttackState 상태 코드엔 없음(있다면 `DealDamageToPlayer`/`attackShape.Execute` 내부).
- 왜 문제: 몹 공격 튜닝 시 경로 선확인 필요, 기본 경로 몹엔 패턴별 슬로우 불가. 버그보다 **구조적 분기**. 트리거: 항상(데이터로 갈림).
- 확실성: 분기 존재 확정("기본 경로에 전무"는 `DealDamageToPlayer` 내부까지 봐야 완전 확정 — 상태 레벨에선 없음 확실).
- 수정방향: 넉백/슬로우를 공용 경로(`ApplyPlayerHit(dmg,kb,slow)`)로 funnel. **난이도 M, 회귀 위험 中**(모든 몹 피격 피드백 영향).
- 선행/의존: `MonsterBase.DealDamageToPlayer()`/`attackShape.Execute` 내부 확인.

**B3 — 패턴 공격 핫패스 GC 할당**
- 위치: `PatternAttackOverrideSO.cs` — `new List<int>` **118/126/132**(SelectPattern), `new Vector2Int[]` **372/374/376/378/385**(GetGridCells), Instantiate VFX **292/455/460**, `GetComponentsInChildren` **295**, `new GameObject+LineRenderer` **481-482**, `new Material` **496**.
- 현상/빈도: **`new List<int>`(118/126/132)가 최핫** — `SelectPattern`은 `PatternChaseState.Update:638`/`PatternAttackReadyState.Update:693,713`에서 **거의 매 프레임** 호출, 유효 패턴 있으면 List 2개/프레임 할당. GridCells/VFX는 **공격당 1회**. `new Material`(496)은 셰이더 인스턴스라 비싸고 **명시 Destroy 없어 머티리얼 릭**.
- 왜 문제: SelectPattern List = 지속 GC 압박. 트리거: List=매프레임(패턴몹 활성 시), 나머지=per-attack.
- 확실성: 할당 실재 확정.
- 수정방향: GridCells `static readonly` 5종 사전정의(S, 위험0); SelectPattern 버퍼는 `PatternRuntime`(578~)에 재사용 리스트+Clear(M, SO 무상태 규칙 준수); 빔 Material 캐싱 or `Destroy(line.material)`(S).
- 선행/의존: SelectPattern 버퍼는 PatternRuntime 리팩터와 함께.

### 부팅 / 씬전환 / 절차생성

**A3 — Backend Awake·Start 이중 초기화**
- 위치: `AppBootstrapper.cs:413-425`(Awake) / `:451-463`(Start). 둘 다 `if(initBackend && !IsBackendInitialized)` 가드.
- 현상: 동일 초기화 코드 이중. Awake 성공 시 static flag로 Start 블록 데드.
- 왜 문제: 순수 중복(hygiene), 기능 무해. Start 도달 조건은 "Awake 실패 후 재시도"뿐(의도적 재시도로도 해석 가능).
- 확실성: 확정(데드코드).
- 수정방향: Start 블록 삭제 or "재시도 백업" 주석. **난이도 S, 위험 낮음**.
- 선행/의존: 없음.

**A4 — CustomSignUp 메인스레드 동기 블로킹**
- 위치: `AppBootstrapper.cs:648`(`CustomSignUp` 동기 반환) vs `:659-678`(`CustomLogin` 콜백/TCS 비동기).
- 현상: 로그인은 콜백→TCS 비동기, 회원가입만 동기 반환값 사용.
- 왜 문제: 네트워크 왕복 동안 메인스레드 블로킹 → 부팅 스톨. 트리거: **특정**(신규 디바이스 최초 실행=회원가입 경로만). 기존 유저 미도달.
- 확실성: 동기 API 확정, 스톨 체감은 **확인필요**(SDK 내부 블로킹 여부).
- 수정방향: SDK에 콜백 오버로드 있으면 TCS 래퍼로 교체. **난이도 S, 위험 낮음**.
- 선행/의존: 뒤끝 SDK 비동기 오버로드 존재 확인. A6와 묶음.

**A5 — HudBootstrapper.LateUpdate에서 Find (정밀화)**
- 위치: `HudBootstrapper.cs` LateUpdate **75-114**, `FindObjectsByType<MonsterBase>` **249**, `Resources.FindObjectsOfTypeAll<Transform>` **371**. ※ 리뷰의 `GameRunBootstrapper.cs:243,268-280`은 **LateUpdate 아님**(부팅 1회 디버그 패널) → 이 항목은 **HudBootstrapper 단독**.
- 현상: `_panelGuardTimer`로 **0.2초마다 1회** 실행. 보스 미바인딩 시 `TryBindAnyActiveBoss`가 MonsterBase 전체 스캔, UIRoot에서 Panel_Combat 못 찾을 때만 `Resources.FindObjectsOfTypeAll<Transform>`(씬 전체+비활성) 폴백.
- 왜 문제: `Resources.FindObjectsOfTypeAll`는 매우 무거움. 트리거: **특정**(보스 미바인딩 + 패널 미발견 지속). 정상 상태(바인딩+패널 정상)에선 가벼움.
- 확실성: 위험 있음(조건부). "매프레임 무거움"은 과장(0.2s 스로틀+조기탈출).
- 수정방향: 보스 바인딩을 스폰 이벤트 구독으로, Panel_Combat 폴백 참조 캐싱. **난이도 M, 회귀 위험 中**(HUD 가시성 하드가드 방어로직).
- 선행/의존: 없음(HudBootstrapper 단독).

**A6 — async void Start 예외 escape + 취소 토큰 없음**
- 위치: `AppBootstrapper.cs:440` `private async void Start()`, 전체 try/catch 없음, await들에 CT 미전달(클래스에 `destroyCancellationToken` 존재).
- 현상: 초기화 await 체인이 던지면 `async void`라 미관측 escape → `IsReady=true`(:565) 미도달 → 로딩 화면 멈춤 가능.
- 왜 문제: 트리거 **특정**(초기화 중 예외). 하위 대부분 내부 try/catch로 방어돼 실제 escape 지점은 제한적(A9). DDOL이라 취소 필요성 낮음.
- 확실성: try/catch 부재 확정. 진입점이라 async void 자체는 불가피.
- 수정방향: Start 본문 try/catch로 감싸기. **난이도 S, 위험 낮음**.
- 선행/의존: A9와 함께(같은 async 흐름).

**A7 — 씬 로드 2중 구현 + GameFlow 휴면**
- 위치: `AppBootstrapper.cs:137-164`(`LoadSceneNoFlowAsync`) ≈ `SceneTransitionManager.cs:20-47`(`LoadSceneAsync`). `startFlow` 필드 `:51`(기본 false), 분기 `:548-563`.
- 현상: 두 메서드 진행률 로직 사실상 동일(차이=StopBgm 호출+onLoaded 콜백 유무). startFlow=false 기본 → GameFlow/SceneTransitionManager 미생성, `RequestLoad`는 항상 NoFlow 경로.
- 왜 문제: 중복→드리프트 위험(위생). 기능 버그 아님. GameFlow 휴면은 "테스트 씬 직접 실행" 목적 의도로 보임.
- 확실성: 중복+기본값 확정. 프로덕션 씬의 startFlow 직렬화값은 **확인필요**(정적 불가).
- 수정방향: `LoadSceneNoFlowAsync`가 `SceneTransitionManager` 재사용하도록 위임. **난이도 S~M, 위험 낮음**.
- 선행/의존: startFlow 정책(프로덕션에서 GameFlow 쓰는지) **설계 결정**.

**A8 — DifficultyTolerance=0.35f 하드코딩**
- 위치: `RunSequencer.cs:60`(`const float 0.35f`), 사용 `:245`.
- 현상: 난이도 윈도 폭 하드코딩(후보 0개면 전체 폴백 `:246`).
- 왜 문제: 서버 CSV(RUN_STRUCTURE) 정본화 정책과 불일치 — 데이터로 조정 불가. 트리거: 항상(튜닝 시 재빌드). 기능 버그 아님.
- 확실성: 확정.
- 수정방향: `IRunStructure`/`RunStructureConfig`에 필드 추가해 CSV/SO 주입. **난이도 S, 위험 낮음**(기본 0.35 유지 시 무변).
- 선행/의존: RUN_STRUCTURE CSV 컬럼 추가와 묶임.

**A9 — Item/Rune/RelicStat init을 WhenAll, 개별 try/catch 없음**
- 위치: `AppBootstrapper.cs:509-513`(Steam), `:525-530`(자동로그인 성공), `:537-541`(폴백). 각 `InitializeAsync`는 CDN을 try/catch로 삼키나 **Addressables 폴백은 try/catch 밖**(예: ItemDataManager:36~, RelicStatDataManager:35~).
- 현상: `WhenAll`은 하나만 throw해도 전체 전파 → A6의 async void로 escape.
- 왜 문제: 트리거 **이론상/특정**(폴백 로드 throw 시). 각 매니저가 CDN 예외를 내부 삼켜 escape 지점은 폴백 throw로 좁음.
- 확실성: 위험 있음(대부분 내부 방어).
- 수정방향: (a) A6와 함께 Start try/catch, 또는 (b) 각 InitializeAsync 폴백까지 try/catch 확장. **난이도 S, 위험 낮음**.
- 선행/의존: A6와 강결합.

**A10 — runFlowController ?? AddComponent 반복 (2곳으로 정정)**
- 위치: `GameRunBootstrapper.cs:763`(StartProcGenRunAsync), `:2372`(ContinueProcGenRunAsync). **2곳**(세 번째 경로 StartNextChapterInSceneAsync는 763 재사용). 필드 선언 `:39`.
- 현상: 필드 비어 있으면 매 호출 새 컴포넌트 부착, 결과를 필드에 캐시 안 함(로컬 `flow`에만).
- 왜 문제: 인스펙터 미할당 + 절차 경로 2회 이상 진입(이어하기 후 챕터 전환 등) 시 RunFlowController 중복 부착 가능. 트리거: **특정**(프리팹 필드 할당돼 있으면 미발생).
- 확실성: 위험 있음(조건부, 프리팹 할당 상태=정적 밖).
- 수정방향: `EnsureRunFlow()` 헬퍼로 통합 + 필드 캐시. **난이도 S, 위험 낮음**.
- 선행/의존: 없음(프리팹 필드 할당 1회 확인 권장).

### 데이터 / 리소스 / 성능

**R1 — Addressable 에셋 회수 경로 데드**
- 위치: `AddressableManager.cs:319/334/349`(ReleaseAllInstances/Assets/All), `AppBootstrapper.cs:94-101`(UnloadChapterAssets). 전수 grep: 외부 호출자 0(내부 상호호출뿐).
- 현상: `_assetHandles`가 로드 성공 핸들을 영구 보관, 어디서도 비우지 않음.
- 왜 문제: 챕터 넘나들어도 이전 팔레트/프리팹/텍스처 핸들 상주(상한 없는 누적). 트리거: **항상**(런 진행 시 단조 증가). 폭주는 아니나 세션 간 회수 없음.
- 확실성: 호출자 0 확정, 실사용 영향은 프로파일링 필요.
- 수정방향: 챕터 전환/런 종료(EndRun/AdvanceChapter)에서 회수. **난이도 M, 회귀 위험 中**(사용 중 핸들 조기 해제 시 핑크/NRE — 풀 클리어 선행).
- 선행/의존: **R2와 한 세트**.

**R2 — 스코프 로드 오버로드 완전 휴면**
- 위치: `AddressableManager.cs:189-202`(`LoadAssetAsync(key,scope)`), `:360-376`(ReleaseScope). `AppBootstrapper.cs:48`(`enableScopedAssetUnload=false`), `:94-101`.
- 현상: 스코프 오버로드 호출자 0 → `_scopeKeys` 안 채워짐 → `ReleaseScope` 항상 no-op. 유일 호출자 `UnloadChapterAssets`도 외부 호출자 0 + 플래그 false 이중 게이팅.
- 왜 문제: R1 방어 장치가 죽어 있음. 트리거: 항상.
- 확실성: 확정.
- 수정방향: 챕터 로드에 scope 태그 전파 + UnloadChapterAssets 실배선 + 플래그 활성(프로파일 후). **난이도 M~L, 회귀 위험 中**.
- 선행/의존: R1과 동일.

**D1(데이터) — MonsterDataManager 컬처 민감 float 파싱**
- 위치: `MonsterDataManager.cs:92-94`(move_speed/attack_range/attack_cooldown). 정정: `float.Parse`가 아니라 `float.TryParse(row[...].ToString(), out ...)` — **culture 미지정은 사실**. 공용 헬퍼 `MapDataManager.cs:228-234`(`TryGetFloat`, InvariantCulture)를 13개 매니저 중 이것만 미사용.
- 현상: 스레드 culture로 파싱(정수는 문제없음).
- 왜 문제: 콤마 소수 로케일(독/프)에서 "1.5"→왜곡/0. 트리거: **특정**(콤마 로케일 기기). en-US/국내면 무증상.
- 확실성: 위험 있음(로케일 의존, 코드 결함 확정).
- 수정방향: `row.TryGetFloat(...)` 경유 or `NumberStyles.Float, CultureInfo.InvariantCulture` 명시. **난이도 S, 위험 낮음**. **빠른 승리**.
- 선행/의존: 없음.

**P1 — MapBuilder 동기 블록 인스턴스화 (정정)**
- 위치: `MapBuilder.cs` Build(24-197)/BuildCeiling(339-376)/BuildRoomLights(384-456) 내부 x·z 루프 yield 없음. 호출부 `GameRunBootstrapper.cs:543-551`(존맵), `:1517-1519`(단일방).
- 현상: 정정 — **존맵 경로는 4존마다 yield 존재**(`:549-550`). 단일방 경로는 Build/Ceiling/Lights 사이·내부 모두 yield 없음(다음 yield는 벽 투명도 1554행).
- 왜 문제: 큰 그리드 방/존 하나를 그리는 동안 프레임 블로킹→히칭. 트리거: **특정**(대형 그리드 방 진입).
- 확실성: 위험 있음(히칭 실측 필요).
- 수정방향: Build를 UniTask화 or x루프 N칸마다 yield. **난이도 M, 회귀 위험 中**(NavMesh 빌드 `:1550`가 Build 완료 가정 → 순서 유지).
- 선행/의존: NavMesh 순서.

**P3 — 풀러 spawn/despawn마다 인터페이스 GetComponent**
- 위치: `ObjectPoolerManager.cs:272`(spawn `GetComponent<IPooledObject>()?.OnSpawn`), `:144`(despawn `?.OnDespawn`). 양 경로 확인.
- 현상: 인터페이스 오버로드 GetComponent, 캐싱 없음.
- 왜 문제: 인터페이스 GetComponent는 비싸고 고빈도 풀링(투사체/이펙트/몹) 시 GC/CPU. 트리거: **특정**(고빈도 풀링 전투). 값은 프리팹당 불변인데 매번 조회.
- 확실성: 위험 있음.
- 수정방향: `PooledObjectInfo`(:146)에 `IPooledObject` 참조를 CreateInstance(:280-291) 시 1회 캐싱. **난이도 S~M, 위험 낮음**. **빠른 승리**.
- 선행/의존: 없음.

**P2 — 폐기 FindObjectsOfType<BoxCollider> 전체 스캔 (빈도 정정)**
- 위치: `DragonPatternFloorUtils.cs:83`(`FindObjectsOfType<BoxCollider>()`, `ResolveArenaBoundsXZ`). 호출자 `DragonBossMonster.InitializeRoomContext:614`(OnInitialized:271, OnEnable:493), `DeathKnightBossMonster:461`.
- 현상: 씬 전역 BoxCollider 스캔. 정정 — "패턴마다"가 아니라 **보스 스폰/활성 시 1~수회**.
- 왜 문제: 전역 스캔+폐기 API 경고. 빈도 낮아 실영향 경미(스폰 순간 스파이크). 트리거: **특정**(보스 스폰).
- 확실성: 이론상~경미(코드 결함 확정, 성능 영향 낮음).
- 수정방향: `FindObjectsByType<BoxCollider>(FindObjectsSortMode.None)` + 결과 캐싱. **난이도 S, 위험 낮음**.
- 선행/의존: 없음.

**D3(item) — ItemDataManager CDN 성공 시 캐시 통째 교체**
- 위치: `ItemDataManager.cs:140-145`(`if(loaded>0){ _itemById=serverData; SaveToJson(); }`). 대조: `MonsterDataManager.cs:81-85`는 행별 stat_version 비교 부분 갱신.
- 현상: 서버 1행이라도 오면 무조건 전체 교체(버전 게이팅 없음).
- 왜 문제: 부분/불완전 CDN 응답 시 로컬 통째 덮어쓸 위험 + 매니저 간 정책 불일치. 트리거: **특정**(CDN 부분 응답). 정상 CDN이면 무해(즉시 반영은 의도).
- 확실성: 이론상(의도 동작이나 불일치는 사실).
- 수정방향: chart 버전 헤더/min-row 게이팅. **난이도 M, 회귀 위험 中**. **확인필요**(CDN 완전성 보장 여부).
- 선행/의존: 정책 통일 결정.

### 세이브 / 무결성 / 텔레메트리

**D1(세이브) — HMAC 경고 전용 + 평문 키**
- 위치: `SaveIntegrity.cs:36`(`const string SecretKey=...` 평문 내장), `LocalFileRunSaveStore.cs:169-171`(Mismatch시 `LogWarning`만 후 그대로 로드).
- 현상: 불일치여도 **거부 안 하고 로드 진행**. 키 빌드 평문.
- 왜 문제: 키 추출로 서명 재계산 가능→탐지 무력. 단 코드/주석이 "캐주얼 변조 탐지 전용, 데이터손실0 우선"을 명시(설계 의도). 트리거: **이론상**(보안 목적만).
- 확실성: 위험 있음(의도된 설계 한계).
- 수정방향: 랭킹 지표는 서버 재계산, HMAC은 탐지용 유지. 로컬 거부 모드는 정품 세이브 `.sig` 마이그레이션 선행 필수(주석 명시). **난이도 S(로직), 회귀 위험 高**(기존 .sig 없는 세이브 거부→손실). 현행 유지 권장.
- 선행/의존: 출시 시점 정책 결정.

**D3(세이브) — masterSeed 미클램프 + TickCount 시드 + seed 텔레메트리 미전송**
- 위치: 시드 `RunFlowController.cs:97`(`_seed!=0 ? _seed : Environment.TickCount`), 미클램프 `SaveSanitizer.cs:57`(의도적 제외 주석), 텔레메트리 `BackendGameData.BuildParam:205-225`(seed 필드 없음).
- 현상: 신규 런 시드=TickCount. 시드/절차상태는 결정성 보존 위해 일부러 클램프 제외. 서버 통계에 seed 없음.
- 왜 문제: TickCount 오버플로/클램프 부재는 **기능 버그 아님**(결정성상 오히려 옳음). **진짜 이슈는 seed 텔레메트리 부재** → 버그 리포트 시 특정 런 재현 불가. 트리거: **특정**(재현 필요 시).
- 확실성: 위험 있음(시드 소스/클램프는 정상, seed 전송 부재만 개선점).
- 수정방향: BuildParam/RUN_PROGRESS에 masterSeed 추가 or 사망 로그에 포함. **난이도 S, 위험 낮음**.
- 선행/의존: D9와 묶음(텔레메트리 스키마).

**D4 — 위치 미저장 + 방 클리어~다음 진입 전 종료 시 보상 중복 가능성**
- 위치: `RunSaveData.cs:16-61`(좌표 필드 전무), 복원 배치 `RunFlowController.ResumeAsync→EnterRoomAsync→MovePlayer(entryPos):307`(항상 입구), 저장 타이밍 `EnterRoomAsync:355`(진입 시), 보상 `ClearRewardTrigger:117/131`(클리어 후).
- 현상: 세이브는 방 진입 1회. 보상은 RunDelta 누적→다음 방 진입 시 저장. 방 클리어+보상 수령 후 다음 방 진입 전 종료하면 진입 시점 세이브(보상 반영 전)로 재개→같은 방 재클리어로 보상 재수령 가능.
- 왜 문제: `RunDelta.GainedGold/Essence/Items`는 다음 저장 경계 전까지 미영속. 트리거: **특정**(클리어 직후~다음 진입 전의 좁은 창). 정상 종료(사망/클리어)는 세이브 삭제라 안전.
- 확실성: 위험 있음(중복 창 실재하나 "그 방 처음부터"라 하데스식 의도 내 손실/재획득으로 볼 여지).
- 수정방향: 보상 수령 직후 추가 저장 경계 or 진입 세이브에 선반영. **난이도 M, 회귀 위험 中**. **D5와 묶어 "저장 경계 정책"** 결정.
- 선행/의존: **기획 결정**. **확인필요**(실제 중복 재현).

**D8 — 복원 시 synergies append + 룬 재계산 순서 의존**
- 위치: `GameRunSession.RestoreFromSaveAsync:285-293`(append), 적용 `:606-607`(`RestoreSynergies`), 1회성 가드 `:257-261`(Phase!=NotRunning 즉시 반환).
- 현상: 복원은 런당 1회(Phase 가드). synergies는 빈 리스트에 append, 룬은 `runeCellsJson` 재계산으로 소스 분리.
- 왜 문제: 이론상 synergies 복원과 룬 재계산이 동일 시너지 이중 적용 위험이나, 1회성 가드 + 소스 분리로 현 구조에선 미관측. 트리거: **이론상**.
- 확실성: 이론상·**확인필요**(복원 후 룬 보드 재편집 상호작용 미확인).
- 수정방향: 복원 후 룬→시너지 재계산 시 `_appliedSynergies` clear 후 단일 소스 재구성. **난이도 M, 회귀 위험 中**. 버그 증거 없어 우선순위 낮음.
- 선행/의존: 없음.

**D9 — 텔레메트리 누적통계뿐 + 실패 시 LogError만**
- 위치: `BackendGameData.BuildParam:205-225`+`UserGameData.cs:5-28`(스키마), `SaveAsync:151-161`/`ApplyRunResultAsync:172-177`.
- 현상: 전송 필드=level/exp/gold/재화/각성 등 **누적통계뿐**(seed/사망원인/방번호/챕터상세 없음). 실패 시 `Debug.LogError`만, 재시도 없음.
- 왜 문제: 밸런싱/버그 분석용 런 상세 부재 + 네트워크 순단 시 통계 유실. 트리거: **특정**(분석 필요/네트워크 실패). 로컬 메타는 선저장(`:139`)돼 로비 재화는 보존.
- 확실성: 확정.
- 수정방향: 상세 스키마(seed/사망사유/도달 방) + 실패 재시도(뒤끝 SendQueue). **난이도 M, 위험 낮음**.
- 선행/의존: D3(세이브)와 묶음. 출시 전.

**D10 — 메타 멀티기기 충돌(LWW 없음)**
- 위치: `BackendGameData.LoadAsync:95-105`(로컬 존재 시 무조건 `Data=local`, 세대/타임스탬프 비교 없음). `UserGameData`에 timestamp 필드 자체 없음.
- 현상: 서버에서 채운 Data를 로컬 있으면 무조건 덮어씀.
- 왜 문제: 기기 A 진행 후 B 플레이 시 B의 오래된 로컬이 서버 최신 덮어 각성/재화 롤백. 트리거: **특정**(멀티기기). 단일 기기 무해. SteamCloud 미착수라 현재 노출 낮음.
- 확실성: 확정(LWW 부재).
- 수정방향: `updatedAt`/generation 추가 후 최신 채택. **난이도 M, 회귀 위험 中**(스키마 마이그레이션). 출시 전(멀티기기 지원 계획 시).
- 선행/의존: 없음.

**D7 — 파일 I/O 전부 동기**
- 위치: `LocalFileRunSaveStore.cs` — `File.WriteAllText:58`/`File.Copy:62`/`File.Move:67`/`File.ReadAllText:164`. `SaveRunLocal`(RunProgressManager:78)도 동기 void.
- 현상: UniTask/CT 미사용 동기 블로킹(비동기 규칙 위반).
- 왜 문제: 세이브 크거나 디스크 느리면 방 진입 시 히치. 트리거: **특정**(규모/디스크 의존). 현재 소형 JSON이라 경미.
- 확실성: 확정(동기 I/O), 성능 영향 이론상.
- 수정방향: `IRunSaveStore`를 UniTask화 + `File.WriteAllTextAsync`+CT(원자적 tmp→bak→move 순서 유지). **난이도 M, 회귀 위험 中**. 우선순위 낮음.
- 선행/의존: 없음.

### 빌드 시스템

**C1 — 무동작 서약 2종이 라이브 롤 풀 포함(트랩 선택지)**
- 위치: `Implementations/LeodegranceCovenant.cs:10-22`, `GuinevereCovenant.cs:8-20`(둘 다 CovenantId/Category/표시문자열만 override, `ICovenant*` 메서드 미override→CovenantBase:80-108 no-op), `WorldCovenantPickup.cs:96/111`(필터 없이 AllIds 12종 롤), `CovenantFactory.cs:56`(AllIds=레지스트리 12키, 스텁 등록 `:36-37`).
- 현상: 두 서약 획득해도 효과 0. 픽업은 스텁 필터 없이 12종 전체를 3지선다 풀로. 설명에 "(준비 중)" 라벨.
- 왜 문제: 3지선다에 뜨면 죽은 선택지(트랩). 트리거: **확률적**(미보유 상태에서 롤될 때). "(준비 중)" 라벨이 완화.
- 확실성: 무동작=확정, 트랩 UX=위험 있음.
- 수정방향: (A) `IsImplemented` 플래그로 풀 제외(**난이도 S**), (B) 실구현(난이도 L). **선행: 기획 결정 필요**(로스터 10종 축소 vs 완성).
- 선행/의존: **기획 결정**.

**C2 — stale 에디터 제너레이터(구버전 4종만)**
- 위치: `Editor/CovenantDataGenerator.cs:23-44`(nimue/arthur/morgana/galahad 4종만 생성, v2 8종 누락), 테이블 덮어씀 `:57-60`.
- 현상: 메뉴 실행 시 테이블을 4종으로 리셋.
- 왜 문제: **런타임 무해**(`#if UNITY_EDITOR`, 조회는 서버 폴백 체인). 위험은 "개발자가 이 버튼으로 최신 테이블 밀어버림".
- 확실성: 확정(4종만 생성).
- 수정방향: v2 8종 추가 or 삭제(수동 authoring 전환). **난이도 S, 위험 없음**(에디터 전용). 삭제 시 사용자 확인.
- 선행/의존: 없음.

**C3 — RuneEffectFactory 주석이 실구현과 모순**
- 위치: `RuneEffectFactory.cs:4-5`(주석 "전기 4종만 실효과, 20종 빈 폴백") vs 본문 `:15-55`(24분기 전부 구체 Effect 반환, `:53` 인라인 주석은 자기정정).
- 현상: 상단 클래스 주석만 stale, 코드는 24종 완성.
- 왜 문제: 코드 무영향, 문서 오독 리스크(미래 개발자 오판).
- 확실성: 확정(주석 stale).
- 수정방향: 상단 주석 갱신. **난이도 S(주석 한 줄), 위험 0**. **빠른 승리**.
- 선행/의존: 없음.

### UI / 이동 / 카메라

**M1 — 에임어시스트 OverlapSphere 힙 할당 (🔴→🟡 하향)**
- 위치: `PlayerController.cs:1498`(`Physics.OverlapSphere(origin,radius)`), `:1505`(콜라이더별 `GetComponent<IDamageable>()??GetComponentInParent`). 호출부 `ActAttackState.cs:520,531`(PlayCurrentComboAnimation), `IasenSlashBehaviorSO.cs:66`.
- 현상: 호출마다 `Collider[]` 할당 + 콜라이더별 GetComponent 2회.
- 왜 문제: 정정 — **프레임당 아니라 공격 스윙(콤보 스텝 진입)당 1회**. 반경 7m 하한(ActAttackState:48)이라 후보 많으면 스윙당 GetComponent 비용 큼.
- 확실성: 위험 있음(할당 확정, 빈도=스윙당 1회).
- 수정방향: `OverlapSphereNonAlloc`+고정 버퍼(`CameraOcclusionFader:135` 패턴), `TryGetComponent`. **난이도 S, 위험 낮음**(버퍼 초과 시 잘림 주의).
- 선행/의존: 없음.

**M2 — 공격/마우스룩마다 Camera.main**
- 위치: `PlayerController.cs:1057`(Attack.started 콜백 `Camera.main.ScreenPointToRay`), `:1567,1569`(TryComputeMouseLookDir 폴백, 클릭 캐시 미스 시만).
- 현상: 공격 입력마다 `Camera.main`(내부 태그 검색).
- 왜 문제: 트리거=입력당(프레임당 아님). 부차 발견 — **:1057 널 가드 없음**(Camera.main null 시 NRE).
- 확실성: 위험 있음(성능 제한적), NRE는 이론상.
- 수정방향: 카메라 참조 Awake/스폰 1회 캐싱. **난이도 S, 위험 낮음**.
- 선행/의존: M4와 같은 파일(카메라 참조 정리).

**U1 — 뷰가 코드로 자기 구성(프리팹 배선 우회)**
- 위치: `CombatPanelView.cs` 20+ 지점(`new GameObject($"..."`): 425/607/730/782/817/830/842/853/896/926/958/1010/1021/1050/1085/1095/1108/1121/1155).
- 현상: 스탯/버프그리드/게이지/툴팁/알림/스킬슬롯 전부 절차 생성 + 이름 문자열 보간.
- 왜 문제: 디자이너 수정 불가, 구성 시점 할당·문자열 GC(프레임당 아님), UI 규칙(프리팹 authoring)과 상충.
- 확실성: 확정(구조적). 성능은 1회성이라 경미.
- 수정방향: 위젯 프리팹 추출+`[SerializeField]` 바인딩, Ensure는 폴백만. **난이도 L, 회귀 위험 高**(레이아웃 수치 하드코딩→픽셀 검증). 출시 후.
- 선행/의존: U3/U5와 같은 파일.

**U3 — SetStats 이모지 문자열 보간**
- 위치: `CombatPanelView.cs:877-878`(`SetText($"⚔ {atk}")`/`$"🛡 {def}"`). 호출 `HudPresenter.cs:365`(RefreshStats), 구독 `:155`(OnChanged).
- 현상: string 2개/호출 힙 할당.
- 왜 문제: RefreshStats가 `OnChanged`마다 실행 — **HP 변동마다 SetStats 동시 재호출**(`:364-365`)이라 전투 중 빈번, 스탯 불변이어도 재생성.
- 확실성: 확정(불필요 GC).
- 수정방향: TMP 숫자 포맷 오버로드 or 이전값 캐시해 변경 시만. **난이도 S, 위험 낮음**. **빠른 승리**.
- 선행/의존: U1(파일 이관 시 함께).

**C1(cam) — GameCameraController FindFirstObjectByType 다수**
- 위치: `GameCameraController.cs` 7개소: 96(Awake)/191/204/649/706/945/1022(전부 `if(_cinemachine==null) Find` 지연init). ※Update/LateUpdate/FixedUpdate엔 **없음**(grep 0).
- 현상: Awake 1회 후 전환 메서드가 캐시 미스 시에만 재탐색.
- 왜 문제: **매프레임 아님**(전환 시점만). vcam 수명 스멜(씬마다 인스턴스 바뀌면 stale 대비 지연 재탐색). 성능 미미.
- 확실성: 확정(빈도=전환 시점). 실질 위험 낮음.
- 수정방향: 씬 로드/바인딩 이벤트에 1회 주입 or 현행 유지(방어적 폴백). **난이도 M, 회귀 위험 中**. 우선순위 낮음.
- 선행/의존: 없음. **확인필요**(vcam 파괴/재생성 여부).

**C2(cam) — occlusionMask 코드 기본값 ~0 vs 메모리 "inert" 모순**
- 위치: `CameraOcclusionFader.cs:24`(`LayerMask occlusionMask = ~0;`), 사용 `:137`(SphereCastNonAlloc).
- 현상: 코드 리터럴 기본값=~0(Everything). 코드상 모든 레이어 스윕.
- 왜 문제: 메모리 노트의 "Nothing이라 inert"는 코드가 아니라 **씬/프리팹 직렬화값**에서 옴. 씬 `m_Bits:0`이면 inert.
- 확실성: 코드 기본값 확정. inert 여부=**확인필요**(씬/@GameRun 프리팹 YAML m_Bits, 정적 불가).
- 수정방향: 씬/프리팹 m_Bits를 오클루더 레이어로 설정. **난이도 S, 위험 낮음**. 어떤 레이어 가릴지 콘텐츠 결정.
- 선행/의존: 씬 직렬화값 실측 + 오클루더 레이어 정책.

---

## 🟢 위생 / 저위험 / 검증완료

**A11** — 임시 검증 로그 잔존: `RunFlowController.cs:252-266`(`[RunStructure검증]` LogResolvedStructure, 주석에 "임시/제거" 명시), DumpRunPlan `:472-487` 호출 `:102,144,195`(`[RunPlan]`). `#if` 가드 없음. 수정: 가드/제거. **난이도 S**. 출시 전 체크리스트.

**A12** — OnDestroy 정리 양호: `GameRunBootstrapper.cs:284-317`(싱글턴 해제→이벤트 해제→무기슬롯 저장(IsRunning 가드)→맵 릴리즈(null 가드)→참조 정리). 문제 없음.

**A1/A2(해결 확인)** — Phase=Running(`GameRunBootstrapper.cs:2155-2157` StartNewRunAsync, BindPlayer 전 호출) / ResolveBossSpawnTable 오프바이원(`GameRunSession.cs:534-539` EnsureChapter 가드 + 호출 `:770`). **둘 다 수정 반영 확정.** 참고: EnsureChapter 가드는 `ChapterId`에 값0 멤버 없다는 전제 의존(enum 정의 1회 확인 권장, 결론 불변).

**B4(검증)** — Ch1 보스 이중등장 가드: `BossSpawner.cs:129-143`(hasOwnEntrance 분기 + activeSelf 가드, 주석 "이중 등장 방지"). 코드 가드 정상. 최종 판정은 Arena_Boss_Ch1 프리팹 ForestGuardian `m_IsActive=0` + FG의 IBossEntrance 미구현에 의존(**에셋 상태=확인필요**). 메모리 `project_ch1_boss_double_appear`→resolved 갱신 권장.

**B6** — DieState 사망 시 rb/collider 재스캔: `DieState.cs:41,53`(GetComponent/GetComponentsInChildren)가 `MonsterBase.cs:181-182,254,264`(`_rb`/`_cachedColliders`, include inactive) 캐시 무시. per-death라 경미. 수정: MonsterBase 캐시 접근 API 노출 후 재사용. **난이도 S**.

**B5(🟡→🟢 하향)** — 보스 HUD Find: `MonsterBase.cs:973,982`(`FindAnyObjectByType<HudPresenter>`)는 전부 lifecycle(스폰/페이즈/사망, per-frame 아님). 실피해 낮음. 수정: HudPresenter 주입. **난이도 M**. 우선순위 낮음.

**C4** — `ResolveMaxStack` 데드코드: `RunItemInventory.cs:166-171`(호출자 0), PlaceItem(`:95-105`)이 maxStack 미강제. 주석(`:74`)도 실제와 불일치. 수정: 제거 or 배선. **난이도 S~M**. **확인필요**(스택 상한 정책).

**C5** — ItemStack/ItemId "(예시)" 주석이나 실사용: `ItemStack.cs:2`(예시 주석) vs 실사용 `GameRunSession.cs:684`(AddItem은 :679), `EndRunResult.cs:15,18,24`, `RunDelta.cs:12`. string itemId(인벤토리)와 int ItemId(결과/델타) 이중 식별. 수정: 주석 정정(S) / 식별 통일(M~L, 세이브 포맷 영향). 

**C6** — CharacterData 전 필드 public: `CharacterData.cs` 다수 public 필드(컨벤션 위반). 단 신규 3필드(qSkillCinematic:151 등)는 규칙 준수(혼재). 수정: `[SerializeField] private`+프로퍼티(필드명 유지). **난이도 L, 회귀 위험 中~高**(직렬화값 유실 주의). 별도 리팩터 태스크.

**C7** — DoT 루프 킬 후에도 remainingTicks 소진: `MonsterStatusReceiver.cs:183-190`, `MonsterBase.TakeSynergyDamage:641`(IsDead 조기반환)로 무해. 수정: `if(owner.IsDead) break`(명료성). **난이도 S**. 우선순위 최하.

**U2(기각)** — BuffAggregator.Collect: `HudPresenter.cs:69-78` 폴링, `BuffViewAggregator.cs:56-63` Collect가 `_scratch` 버퍼 재사용(`:24,58,62`), `_hasDynamicBuffSources` false면 조기 반환. **List 할당 없음 — 리뷰 주장 오류, 조치 불필요.**

**U5** — 폴백 폰트 Resources.Load: `CombatPanelView.cs:457`(TMP 내장 LiberationSans, static 캐시 1회). 규칙 위반이나 기능/성능 무해. 수정: Addressable/직렬화 폰트. **난이도 S**. 우선순위 낮음.

**U6** — UI 토글 레거시 Input: `UI_ShopPanel.cs:48`(`Input.GetKeyDown(Escape)` in Update). 신 Input System 혼용. 수정: `Keyboard.current.escapeKey` 등. **난이도 S**. **확인필요**(Active Input Handling=Both인지).

**M4** — 카메라 null이면 Update 전면 정지: `PlayerController.cs:692`(3조건 조기반환). 카메라 미바인딩 프레임에 틱/FSM 유실 가능. 수정: 카메라 로직만 국한 가드. **난이도 M, 회귀 위험 中**.

**C3(cam)** — 오클루더별 new Material: `CameraOcclusionFader.cs:203,214`(생성)/`262-266`(Destroy), `_fading` 가드로 재생성 방지. 오클루더 진입 시점 GC. **C2(cam) 선행**(마스크 Nothing이면 이 경로 비활성). 수정: 페이드 머티리얼 풀. **난이도 M**.

**C5(cam)** — Dragon 탑다운 async lerp 데드코드: `GameCameraController.cs:388-417,450-502`(async 2개, 호출자 0, 스냅 버전으로 대체). 수정: 메서드만 제거(`_topDownAscendCts/_topDownReturnCts` 필드는 스냅 버전 :425에서 Cancel하므로 유지). **난이도 S**. 삭제 전 사용자 확인.

**P5** — data manager/ChartLoader raw Debug.Log 미스트립: `ChartLoader.cs:94,141`, `ItemDataManager.cs:109`, `MonsterDataManager.cs:22,25,26,31,66,122`(vs 스트립 래퍼 `RFLog.cs:13-14` `[Conditional]`). 저빈도라 경미. 수정: `RFLog.D` 치환. **난이도 S**.

**A1(arch)** — .asmdef 없음(모놀리식): `Assets/**/*.asmdef` 0개. 한 줄 수정에 전체 재컴파일. 런타임 버그 아님. 수정: 시스템 단위 분리. **난이도 L**. 출시 후.

**A5(F)** — 구(JSON)/신(SO) 몬스터 데이터 경로 공존: `MonsterDataManager.cs:9`(TODO) + `ServerMonsterStatDataManager.cs`. 두 차트/JSON 공존. 수정: SO 단일 통합. **난이도 M**.

**A3(F) — ✅ 해소(확인필요 해제)** — 삭제 셰이더/머티리얼 참조: git status의 Mat_MonsterRim/Silhouette·MonsterRim/Silhouette.shader는 **삭제 아니라 `_Legacy/Graphics/`로 GUID 보존 이동**. 씬/프리팹/렌더러 참조 0. URP RenderObjects 피처(MonsterRim/Silhouette 이름)의 overrideMaterial은 **별도 현행 머티리얼**(Mat_MonsterOutline/Occluded, Art/Materials 실존)을 가리킴. **핑크 위험 없음.** `_Legacy` 4종은 순수 미사용(콘텐츠 정리 대상, 위험 아님).

---

## 심각도 요약 & 권장 처리 순서

**🔴 (2):** D5(종료 저장 훅·기획결정) · B1(DragonBoss timeScale·LEE 협의)
**🟡 (전투 B2·B3 / 부팅 A3·A4·A5·A6·A7·A8·A9·A10 / 리소스 R1·R2 / 데이터 D1(mon)·D3(item) / 성능 P1·P2·P3 / 세이브 D1·D3·D4·D7·D8·D9·D10 / 빌드 C1·C2·C3 / UI M1·M2·U1·U3·C1cam·C2cam)**
**🟢/위생:** A11·A12·B4·B5·B6·C4·C5·C6·C7·U2(기각)·U5·U6·M4·C3cam·C5cam·P5·A1arch·A5F·A3F(해소)
**해결 확인:** A1·A2

### 권장 처리 순서
1. **[🔴 기획결정]** D5 저장 경계 정책(quit/pause 스냅샷 vs 방경계-only) — **D4와 묶어** 결정 + QA.
2. **[🔴 코드]** B1 DragonBoss 히트스톱 → TimeScaleArbiter 경유(LEE 협의).
3. **[🟡 데이터 무결성]** D1(mon) InvariantCulture 파싱(로케일 버그).
4. **[🟡 성능]** P3 풀러 GetComponent 캐싱 → M1 OverlapSphereNonAlloc → P1 MapBuilder 청킹 → P2 보스 Bounds 캐싱/API 교체.
5. **[🟡 UX/기획]** C1 무동작 서약 2종 롤 제외(트랩 제거).
6. **[🟡 메모리]** R1/R2 Addressable 회수(로비/챕터 전환 시 ReleaseAll) — 한 세트.
7. **[🟡 위생]** A3/A7 부팅 이중 init·씬로드 중복 정리, C3/RuneEffectFactory·C5 주석 정정, A11 임시 로그 제거.
8. **[출시 전]** D10 메타 LWW · D9/D3 상세 텔레메트리(seed 포함) · D1(세이브) 랭킹 서버 재계산 · ChartLoader ClearCache 경계.
9. **[메모리 갱신]** `project_ch1_boss_double_appear`→resolved · `project_run_phase_not_running_hub_bug`→fixed · `project_camera_occlusion_inert`(코드 기본 ~0, inert는 씬값).

### ⚡ 빠른 승리 (저비용 S · 고효과 · 회귀 위험 낮음)
| 항목 | 효과 | 근거 |
|---|---|---|
| **D1(mon)** InvariantCulture 파싱 | 로케일 크래시/데이터 손상 제거 | MonsterDataManager.cs:92-94 |
| **B1** TimeScaleArbiter 경유 | 🔴 timeScale 고착/언포즈 버그 제거 | DragonBossMonster.cs:544-553 |
| **P3** 풀러 GetComponent 캐싱 | 고빈도 풀링 GC 제거 | ObjectPoolerManager.cs:144,272 |
| **M1** OverlapSphereNonAlloc | 스윙당 GC 제거 | PlayerController.cs:1498,1505 |
| **B3** GridCells static + 빔 Material Destroy | 패턴 GC + 머티리얼 릭 | PatternAttackOverrideSO.cs:372-385,496 |
| **U3** SetStats 문자열 캐싱 | HP 변동마다 GC 제거 | CombatPanelView.cs:877-878 |
| **C3/RuneEffectFactory 주석 정정** | 문서 오독 제거 | RuneEffectFactory.cs:4-5 |
| **P2** FindObjectsByType 교체 | 폐기 API 경고 해소 | DragonPatternFloorUtils.cs:83 |
| **A11** 임시 로그 가드 | 릴리즈 콘솔 노이즈 제거 | RunFlowController.cs:252-266 |

### 확인 필요 (정적 한계 — 인게임/에디터/씬 실측)
- **C2cam** 씬/@GameRun 프리팹 `occlusionMask` m_Bits(inert 여부)
- **C1cam** vcam 파괴/재생성 여부
- **A4** 뒤끝 SDK CustomSignUp 실제 블로킹/비동기 오버로드 존재
- **A7** 프로덕션 씬의 `startFlow` 직렬화값
- **A9** InitializeAsync Addressables 폴백 예외 전파 여부
- **B4** Arena_Boss_Ch1 ForestGuardian `m_IsActive` + IBossEntrance 미구현
- **C4** 아이템 스택 상한 정책 · **D3item** CDN 완전성 보장
- **D4** 방 클리어~다음 진입 전 종료 시 보상 중복 재현 · **D8** 룬 재편집 시 시너지 이중 적용
- **U6** 프로젝트 Active Input Handling(Both 여부)

---
*생성: 2026-07-02 · 읽기 전용 재검증 · 코드/에셋 미수정 · 컴파일 에러 0 확인.*
