# 하데스식 세이브 시스템 설계 (RelicFairy) — 확정본 v2

> 상태: 설계안 (코드 미구현). 작성 2026-06-06 / 갱신 2026-06-06.
> **방향 확정**: 세이브(진행 중 런 + 메타 영속) = **로컬 파일 + Steam Cloud**. 뒤끝(BACKND) = **텔레메트리(추적/랭킹/라이브옵스, 런 결과 집계만 비동기 전송)**.
> 제약: 방 경계 자동저장 · 중단/이어하기 · 런/메타 분리 · 결정성. 절차생성(`RunFlowController`) 구조 정합.

---

## 0. 핵심 요약

- **뒤끝은 스텁이 아니라 실통합**(에셋 DLL `Assets/TheBackend/Plugins/Backend.dll`, manifest 아님)이고 실제 호출된다. 현재 세이브는 **100% 뒤끝 서버**(`USER_RUN_PROGRESS`/`USER_DATA`) — 로컬 런 세이브는 없다.
- **그러나 로컬 파일 영속 패턴은 이미 프로젝트 전반에 성숙해 있다**: 거의 모든 CDN 캐시(`DataManagers/*`, `MapDataManager`, `ZoneLayoutManager`, `BuffDataManager`, `RelicAwakeningDataManager`)가 `Application.persistentDataPath` + `File.WriteAllText` + `JsonUtility.ToJson` 패턴. **퀘스트 진행은 실제로 `PlayerPrefs` + Newtonsoft(`JObject`)로 로컬 저장**(`QuestManager.Save/Load`). → 이번 설계는 **새 패턴이 아니라 기존 로컬-파일 패턴을 세이브에 적용**하는 것.
- Newtonsoft(`com.unity.nuget.newtonsoft-json 3.2.2`)·Steamworks.NET(`com.rlabrecque.steamworks.net`) 모두 manifest에 존재. persistentDataPath 실제 경로(이 머신): `C:\Users\u\AppData\LocalLow\DefaultCompany\Project_A`.
- 변하지 않는 두 핵심 문제(저장소와 무관):
  1. **활성 절차생성 흐름 `RunFlowController`에 세이브 연결이 전혀 없다.** 이어하기 분기(`GameRunBootstrapper.cs:225`)는 폐기된 zone-layout 경로(`ContinueZoneLayoutRunAsync`)를 탄다.
  2. **결정성 불가**: 시드 미저장 + `RunFlowController.cs:82` `Environment.TickCount` 폴백 + 다수 시스템이 전역 `UnityEngine.Random` 사용.
- **룬 보드(`BoardManager`)는 셀 배치 스냅샷 API가 없다**(`_globalPlacements`/`_sharedShapes` 휘발). 신규 Capture/Restore 필요.

---

## 1. 뒤끝 실통합 여부 + 현재 저장 매체 (1단계 확정)

### 1.1 뒤끝 SDK 통합 — 실통합 (에셋 임포트)

- SDK: `Assets/TheBackend/Plugins/Backend.dll` (+ `TheBackendSettings.dll`, Android `Backend.aar`). **`Packages/manifest.json`에는 없음** → 에셋 직접 임포트 방식(사용자 우려 적중).
- 실제 API 호출처(스텁 아님):
  - `AppBootstrapper.Awake` L374 `Backend.Initialize()`; `DeviceAutoLoginAsync`/`TryCustomLoginAsync` L610-637 `Backend.BMember.CustomSignUp/CustomLogin`.
  - `RunProgressManager`: `Backend.GameData.GetMyData/Insert/UpdateV2` (`LoadAsync` L77, `SaveAsync` L137/148, `ClearAsync` L171).
  - `BackendGameData`: 동일 `Backend.GameData.*` (`LoadAsync` L55, `InsertAsync` L101, `SaveAsync` L133).
  - 기타 호출처: `SteamLoginService.cs`, `UI_Login.cs`, `DevAutoLoginManager.cs`, `RegisterAccount/FindPw/FindID/UserInfo/Nickname.cs`. (`BackendManager.cs`는 死 스크립트로 2026-06-27 제거)
- 결론: **뒤끝 = 실통합 + 로그인/세이브 실사용 중**. 텔레메트리로 역할 축소 시 **세이브 경로만 분리**하면 되고 로그인/계정 인프라는 그대로 둘 수 있다.

### 1.2 현재 세이브 매체 — 100% 서버 (로컬 런/메타 세이브 없음)

- 런 세이브: `RunProgressManager` → `USER_RUN_PROGRESS` 테이블, 3슬롯. 로컬 파일 미사용.
- 메타: `BackendGameData`/`UserGameData` → `USER_DATA` 테이블. 로컬 파일 미사용.
- 직렬화: 쓰기 `JsonUtility.ToJson`+래퍼, 읽기 뒤끝 `LitJson`. **세이브에는 Newtonsoft/persistentDataPath 미사용.**

### 1.3 로컬 파일 영속 패턴은 이미 성숙 (재활용 핵심)

> 세이브엔 안 쓰지만, **CDN 캐시·퀘스트 진행은 이미 로컬에 쓴다.** 패턴/유틸을 그대로 차용 가능.

- `persistentDataPath` + `File.WriteAllText/ReadAllText` + `JsonUtility.ToJson(col, true)` 패턴 사용처(전부 CDN 캐시):
  `DataManagers/`{Block,Chapter,Covenant,Item,Player,ServerEquipment,ServerMonsterStat}DataManager, `MapDataManager`, `ZoneLayoutManager`(L352-379), `BuffDataManager`(L16/135/153), `RelicAwakeningDataManager`(L19/125/143), `MonsterDataManager`.
- **실제 진행 상태 로컬 저장 선례**: `QuestManager.Save()` L128-138 → `PlayerPrefs.SetString` + Newtonsoft `JObject`/`JArray`(`Quest.ToSaveData()` DTO). `Load()` L173-185 `JObject.Parse`. → **Newtonsoft 기반 상태 직렬화가 이미 운용 중**.
- `SoundManager`: `PlayerPrefs`(볼륨). `ServerCacheCleaner`(Editor): persistentDataPath 캐시 관리 메뉴.
- 시사점: **런/메타 세이브는 `persistentDataPath` JSON 파일로 두는 게 프로젝트 관례와 일치**. 직렬화는 JsonUtility(기존 DTO 재활용) 또는 Newtonsoft(QuestManager 선례) 둘 다 가능.

### 1.4 변하지 않는 구조적 사실 (저장소 무관 — v1에서 유지)

| 사실 | 근거 |
|---|---|
| 저장 시점은 이미 방 경계 | `CheckpointZone.OnTriggerEnter`→`SaveAsync` (`CheckpointZone.cs:55-79`), `ClearRewardTrigger.cs:143-146`. 단 legacy World/zone 경로에만 배선. |
| 활성 절차 흐름에 세이브 미연결 | `RunFlowController`(`_sequencer`/`_rng`/`_heading`/`_current` 휘발). 저장/복원 훅 없음. |
| 이어하기 분기가 레거시 | `GameRunBootstrapper.cs:225` → `ContinueZoneLayoutRunAsync`(폐기된 zone-layout). |
| 런 상태 복원은 부분만 | `GameRunSession.RestoreFromSaveAsync`(`GameRunSession.cs:239`) = HP/Gold/PlacedItems/Synergies/RoomLogs만. 방그래프/RNG/시퀀서/룬보드 없음. |
| 런/메타 분리 라이프사이클 존재 | 런폐기 `AppBootstrapper.EndRun`→`ClearAsync`; 메타반영 `HandleRunEnded`→`ApplyRunResultAsync`(`AppBootstrapper.cs:97-106`). |
| 시드 미저장 + 비결정 RNG | `RunFlowController.cs:82` TickCount; 전역 `UnityEngine.Random`: `BuffRoller`/`ShopCatalogSO`/`RoomClearGate`/`MapBuilder`/`CorridorBridgeSpawner`/`BlockPalette`. |

### 1.5 룬 보드 상태 — 스냅샷 API 없음 (확인 완료)

- `BoardManager.cs` 정독 결과: 배치 상태는 `_globalPlacements`(`Dictionary<Shape, GlobalPlacement{grid, squares, anchoredPosition}>` L153) + `_sharedShapes`(`List<Shape>` L145)에 **메모리로만** 존재. 직렬화/복원 메서드 **없음**.
- 배치 단위 `Shape`는 `ItemData`(RuntimeItemData: instanceId/shapeId), `cellOffsets`, `cellSize` 보유. `OnShapePlaced` L506이 점유 `GridSquare` 목록을 기록.
- **anchoredPosition은 UI 좌표·스케일 의존**이라 그대로 저장 부적합 → **점유 셀 좌표(GridSquare row/col)** 기반 직렬화 필요. **확인 필요**: `GridSquare`가 행/열 인덱스를 노출하는지(`GridSquare.cs` 미정독).
- 시너지 *결과*(SynergyRecord)는 `GameRunSession._appliedSynergies`로 저장되지만, *배치 레이아웃*은 손실 → 복원 시 보드는 빈 상태(§3 PR3).

---

## 2. 로컬 + Steam Cloud 세이브 설계 (확정본)

### 2.1 저장 매체 / 파일 레이아웃

- 디렉터리: `Application.persistentDataPath` (이 머신: `…/AppData/LocalLow/DefaultCompany/Project_A`).
- 파일(런 1슬롯):
  - `run_save.json` — 진행 중 런 1개(없으면 파일 부재 = 이어하기 없음).
  - `meta_save.json` — 영구 메타(해금/통화/각성/서약진행/내러티브 플래그/통계).
  - (선택) `run_save.bak`/`meta_save.bak` — 원자적 쓰기용 임시본(temp→rename, 손상 방지).
- 원자적 저장: `…tmp` 기록 → `File.Replace`/rename. 쓰기 중 종료 시 구파일 보존.

### 2.2 직렬화 (기존 DTO 재활용)

- **권장: 기존 `RunSaveData` DTO + `JsonUtility`를 로컬 파일에 그대로 사용** (CDN 캐시 매니저들과 동일 패턴). 뒤끝 `Param`/`LitJson` 종속만 제거.
  - 장점: `BuildSaveData`/래퍼(`ItemListWrapper` 등) 거의 그대로. 학습비용 0.
  - 단점: `JsonUtility`는 다형성/Dictionary 미지원 → cooldowns/board 같은 맵은 KV 리스트로 평면화(§v1 DTO대로).
- **대안: Newtonsoft**(QuestManager 선례). Dictionary/중첩 자유롭지만 DTO 재작성 비용. → **MVP는 JsonUtility, 복잡 구조(board/cooldowns) 많아지면 Newtonsoft 검토.**
- 신규 필드(v1과 동일, 요약): `saveVersion`, `runMetaJson`(RunMetaDTO: seed/visitCount/phase/shop·eventUsed/cooldowns/heading/anchorToggle/currentRoomPoolKey/relicKey/covenantIds), `boardJson`(BoardStateDTO), `stagingItemsJson`, `weaponStateJson`, `runEssence`.
- 메타 DTO: 기존 `UserGameData` 필드(`UserGameData.cs`)를 로컬 `meta_save.json`으로 그대로 이전(재화/통계/각성/lichEncounterCount). 신규 추가 여지: 서약 영구진행·내러티브 플래그.

### 2.3 저장소 추상화 (교체 가능하게)

```csharp
interface IRunSaveStore  { bool HasSave(); RunSaveData Load(); void Save(RunSaveData d); void Delete(); }
interface IMetaSaveStore { UserGameData Load(); void Save(UserGameData d); }
```

- `LocalFileRunSaveStore` / `LocalFileMetaSaveStore` — persistentDataPath JSON.
- `RunProgressManager`는 **로컬 스토어로 재구현**(뒤끝 CRUD 제거)하거나, 얇은 어댑터로 `IRunSaveStore`에 위임. DTO·슬롯 개념 재활용.
- 이점: 추후 콘솔/모바일 등 다른 매체로 교체 용이, 테스트 용이.

### 2.4 저장 시점 (방 경계, 절차 흐름에 재배선)

- **주 훅**: `RunFlowController`가 방 진입을 확정하는 자리 — `TransitionAsync`의 `_sequencer.CommitEntry(plan)`(`RunFlowController.cs:438`) 직후 `EnterRoomAsync` 성공 시점. **이유**: 시퀀서/RNG/heading이 여기 있어 같은 자리에서 스냅샷해야 정합.
  - 첫 방은 `StartRunAsync`의 첫 `EnterRoomAsync` 직후 1회 저장.
- `CheckpointZone`은 절차방 프리팹에 부착해 동일 시점을 트리거하는 대안(택1, 이중 저장 금지).
- **전투 중 비저장**: 방 클리어/진입 직후의 안전 시점에만. 사망 직전 저장 악용 불가.

### 2.5 복원 흐름 (zone-layout → procgen 교체)

```
부팅(AppBootstrapper.Start) → LocalFileRunSaveStore.HasSave()?
  ├ 아니오 → 로비/베이스캠프 정상
  └ 예 → (UI '이어하기') AppBootstrapper.RequestRestoreRun()
        RequestRestoreRunAsync 재배선(AppBootstrapper.cs:206~):
          · 로컬 run_save.json 로드 (뒤끝 Saves[] 대신)
          · isInStartRoom → 폐기 후 새 시작 (기존 정책 유지)
          · Relic/Covenant/Character/Weapon SO 로드 (relicKey/covenantIds 추가)
          · new GameRunSession().RestoreFromSaveAsync(save)  ← 유지
          · BeginRun + RequestLoad(GetSceneForChapter)
        GameRunBootstrapper.Start:225 분기 교체:
-         await ContinueZoneLayoutRunAsync(...)
+         await ContinueProcGenRunAsync(...)   // 신규
```

`ContinueProcGenRunAsync` 순서:
1. RunMetaDTO 역직렬화 → Relic/Covenant 복원.
2. 플레이어 스폰 + `BindPlayer`(HP/스탯/시너지는 RestoreFromSaveAsync가 채움). **중복 적용 가드**: `RefreshAwakening`/`RestoreSynergies`(`GameRunSession.cs:548/557`)가 두 번 안 돌도록.
3. `MerlinRuneBridge.InitializeGridsFromServer` → 신규 `BoardManager.RestoreState(boardJson)` → `_appliedGridIds`/`_appliedThresholds` 선세팅(시너지 2중 적용 차단).
4. 신규 `RunFlowController.ResumeAsync(meta)`: `meta.seed`로 `_rng`/`RunSequencer` 재생성 → `RunSequencer.RestoreState(visitCount/phase/shopUsed/eventUsed/cooldowns)` → `meta.currentRoomPoolKey`로 현재 방 즉시 재빌드(전환 연출 없이) → 플레이어 배치 → 출구 게이트 즉시 공개(`RollExits`).
5. `RequestHudMode(Combat)`.

복원 시점 정책(택1): **(A) 방 진입 직후 = 전투 재시작**(하데스 근접, 권장) / (B) 방 클리어 후 = 출구 선택 재시작. 저장 시점과 일관되게.

### 2.6 런/메타 분리 (로컬 두 파일)

- 런: `run_save.json`. 방 경계마다 갱신. **사망/클리어 시 삭제**(`IRunSaveStore.Delete`) — 기존 `EndRun`/`ClearAsync` 자리를 로컬 삭제로 교체.
- 메타: `meta_save.json`. **사망/클리어 시 갱신**(통화/통계/각성/해금) — 기존 `ApplyRunResultAsync`(`UserGameData.ApplyRunResult`) 로직 그대로, 저장만 로컬로.
- 중단(앱 종료)은 삭제가 아니므로 run_save.json 잔존 = 이어하기 대상(정상).
- 주의: `EndRun`(런 삭제)과 `HandleRunEnded`(메타 갱신)가 별 트리거 → 사망 경로에서 둘 다 1회씩 보장 검증.

### 2.7 RNG 결정성

- 마스터 시드: 런 시작 시 1회 확정(현 TickCount → 확정값 `meta.seed` 저장). 복원 시 주입.
- **`System.Random`은 상태 직렬화 불가** → 권장: **방별 자식 시드** `childSeed = Hash(masterSeed, visitCount)`로 매 방 전용 `new System.Random(childSeed)`. `visitCount`만 저장하면 결정적 재현(소비 카운트 추적 불필요). 시퀀서 출구 롤·룸 미러링·룸빌더에 적용.
- 전역 `UnityEngine.Random` 사용처(상점/보상/버프/맵회전/복도/팔레트)를 결정성 원하면 동일 자식 시드 `System.Random`으로 라우팅 → **범위 넓어 후속 PR(§3 PR6)**. MVP는 "방 토폴로지/출구까지만 결정적"인 **부분 결정성** 허용.

### 2.8 Steam Cloud 매핑

- Steamworks.NET 보유. 두 방식:
  - **(권장, MVP) Auto-Cloud**: Steamworks 파트너 설정에서 루트(예 `%WinAppDataLocalLow%/DefaultCompany/Project_A/`) + 패턴(`run_save.json`, `meta_save.json`) 등록. **코드 0** — 로컬에 쓰기만 하면 Steam 클라가 자동 동기화. 단 회사/제품명 확정 필요(현재 `DefaultCompany/Project_A` → 출시 전 변경 시 경로 갱신).
  - **(정밀) ISteamRemoteStorage**: `SteamRemoteStorage.FileWrite/FileRead`로 명시 동기화. 충돌/쿼터/플랫폼 일관 제어. 코드 추가.
- 충돌: 단일 기기 가정이면 Auto-Cloud로 충분. 멀티기기 동시 플레이 충돌은 메타에 `savedAt`/세대 카운터로 최신 우선(LWW). → 정밀 제어 필요 시 ISteamRemoteStorage 전환(PR4).
- 손상/버전: `saveVersion` 분기 + 파싱 실패 시 `.bak` 폴백 + 그래도 실패면 신규 시작(메타는 보존 우선).

### 2.9 뒤끝 텔레메트리 분리

- 역할 축소: 뒤끝은 **세이브 권위 아님**. 런 종료 집계만 **비동기 fire-and-forget**(실패 무시, 게임 진행 무차단)로 전송.
- 재해석: `BackendGameData.ApplyRunResultAsync`(`BackendGameData.cs:154`) → **텔레메트리 POST**로 의미 변경. 전송 지표(예): 챕터 도달, 클리어 여부, 소요시간, 사망 원인/방번호(visitCount), 획득 골드/정수, 선택 유물/서약, 시드(밸런싱 재현). `UserGameData`의 누적통계(totalRuns/Clears/highestChapter)는 **로컬 권위 + 서버 미러**.
- 기존 `USER_RUN_PROGRESS`(런 세이브 테이블)는 **세이브 용도 폐기** → 텔레메트리 이벤트 테이블로 재설계하거나 제거. `RunProgressManager` 서버 CRUD 제거.
- 로그인(`Backend.BMember.*`)/계정 인프라는 **유지**(랭킹·라이브옵스 식별자). Steam 로그인과 연동.

### 2.10 기존 코드 재활용 / 신규 / 제거

**재활용(거의 그대로)**: `RunSaveData`+래퍼, `GameRunSession.RestoreFromSaveAsync`, `UserGameData`(ApplyRunResult 로직), `AppBootstrapper.RequestRestoreRunAsync` 골격, 방경계 저장 트리거 패턴, persistentDataPath+JsonUtility 유틸 패턴(DataManager류 참고), 로그인/계정.

**신규**: `IRunSaveStore`/`IMetaSaveStore` + `LocalFile*Store`, `RunMetaDTO`/`BoardStateDTO`/`WeaponStateDTO`, `RunSequencer.RestoreState`+자식시드, `RunFlowController.ResumeAsync`+시드확정/저장, `BoardManager.CaptureState/RestoreState`(+`GridSquare` 좌표 노출 확인), `ContinueProcGenRunAsync`, 텔레메트리 클라이언트(뒤끝 비동기 전송).

**로컬로 전환**: `RunProgressManager`(서버 CRUD→로컬 스토어), `BackendGameData` 메타 저장→로컬 meta_save.json.

**제거/폐기**: `USER_RUN_PROGRESS` 세이브 용도, `ContinueZoneLayoutRunAsync` 복원 경로(절차 흐름으로 대체 — 단 zone-layout 자체가 쓰이는 다른 경로 없는지 **확인 필요**).

---

## 3. 구현 단계 (PR)

| PR | 범위 | 검증 | 위험 |
|---|---|---|---|
| **PR1 (로컬 런 이어하기 MVP)** | `IRunSaveStore`+`LocalFileRunSaveStore`(run_save.json, 원자적 쓰기) + RunMetaDTO 최소(seed/visitCount/phase/shop·eventUsed/cooldowns/heading/currentRoomPoolKey) + `RunSequencer.RestoreState`(자식시드) + `RunFlowController.ResumeAsync`/시드확정·저장 + 방경계 저장 훅 + `GameRunBootstrapper:225` → `ContinueProcGenRunAsync` | 방 3개 진행 → 강제종료 → 재실행 → 동일 챕터·방종류·출구·HP/Gold/아이템 재개 | 시드/visitCount 정합, 현재방 재빌드 시 추락/게이트 중복, 파일 손상 |
| **PR2 (로드아웃 완전 복원)** | relicKey/covenantIds/weaponStateJson(슬롯index)/stagingItemsJson 저장·복원 | 유물+서약+서브무기 선택 후 이어하기 → 패시브/스킬/서약 동일 | RefreshAwakening/RestoreSynergies 중복 가드 |
| **PR3 (룬 보드 복원)** | `GridSquare` 좌표 노출 + `BoardManager.Capture/RestoreState`(점유 셀 기반) + boardJson + 적용 가드 복원 | 배치 후 이어하기 → 동일 배치·시너지·중복 미적용 | 셀 좌표/회전 스키마, 시너지 2배 |
| **PR4 (Steam Cloud)** | Auto-Cloud 매핑(파트너 설정) 또는 ISteamRemoteStorage 래핑 + 충돌/버전/.bak | 2기기 동기화, 손상 파일 폴백 | 회사/제품명 확정, 멀티기기 LWW |
| **PR5 (뒤끝 텔레메트리 분리)** | `ApplyRunResultAsync`→비동기 텔레메트리, `RunProgressManager` 서버CRUD 제거, 메타 로컬화, 이벤트 스키마 | 런 종료 시 지표 전송(실패해도 진행 무영향), 오프라인 정상 플레이 | 서버 의존 잔재, 오프라인 가드 |
| **PR6 (결정성 강화, 선택)** | 전역 `UnityEngine.Random` 사용처를 방 자식시드 `System.Random`으로 | 동일 시드 두 런 → 보상/상점/버프 동일 | 광범위 리팩터, 부분 결정성으로 축소 가능 |

권장: **PR1 = "끄고 켜면 이어하기"의 본질**. PR4(Cloud)는 PR1 직후 끼워도 됨(로컬 파일만 있으면 Auto-Cloud는 코드 0). PR5는 뒤끝 의존 정리.

---

## 4. 하데스 모델과의 차이/주의

- **격리형 방(leapfrog)**: 한 번에 1방만 존재(`RunFlowController.cs:119`) → 복원 시 그래프 전체가 아니라 **현재 방 1개만** 재빌드. 저장 부담 작음.
- **visitCount 기반 진행**: 하데스 "방 번호" 등가. 난이도/페이즈 결정키 → 자식시드 파생키로 적합.
- **룬 퍼즐**: 하데스에 없는 고유 상태. 현 구조는 시너지 효과만 보존·퍼즐 진행 손실 → 이어하기 후 재배치 강요 시 UX 저하. **PR3 우선순위 기획 판단 필요.**
- **로컬 권위 + 텔레메트리**: 하데스도 로컬 파일. 단 RelicFairy는 랭킹/라이브옵스용 서버 동시 운용 → "세이브=로컬/Cloud, 통계=서버 미러"의 **이중 기록**을 명확히 분리(권위는 로컬).
- **중간 저장 금지 일관성**: 방 경계만 저장 → 죽음 직전 저장 악용 불가(하데스 동일).

---

## 4.5 PR1 구현 메모 (2026-06-06 구현 완료, 컴파일 0 에러)

- **저장 매체**: `persistentDataPath/run_save.json` (원자적 tmp→교체 + `.bak` 폴백, JsonUtility). `LocalFileRunSaveStore : IRunSaveStore`.
- **권위 분리**: 진행 중 런 = 로컬. 뒤끝 `USER_RUN_PROGRESS`(SaveAsync/ClearAsync) 코드는 **무변경**(휴면). 복원/표시/삭제는 로컬 경유.
- **저장 시점**: `RunFlowController.EnterRoomAsync` 성공 말미(방 경계, 첫 방 포함). 이어하기 재생성 중(`_resuming`)엔 생략.
- **결정성**: `RunSequencer`가 방별 자식 시드 `Combine(masterSeed, visitCount)`로 출구 롤, 미러도 동일 방식. `masterSeed`+`visitCount`+시퀀서 상태 저장 → 이어하기 시 동일 방/출구 재현. 단 상점/보상/버프 등 전역 `UnityEngine.Random`은 **미적용**(PR6).
- **룬 보드(핵심 결정)**: **점유 셀(col,row) 단일 진실원본**으로 저장. 복원은 `RestoreSynergies(null)`+`ClearAppliedSynergies()`+`ClearAppliedGrids()`로 레코드 기반 적용을 초기화한 뒤 `MerlinRuneHexGridView.RestoreOccupiedCells` → `RefreshPlacedCells` 재계산으로 **스탯+메커닉을 1회만** 재적용. 이유: `PlayerRuntimeStats.RestoreSynergies`가 메커닉(ChargingStrike 등)을 복원하지 못해 레코드 단독 복원은 불완전 — 점유 재계산이 더 충실.
- **복원 분기**: `GameRunBootstrapper.Start`의 `ContinueZoneLayoutRunAsync` → `ContinueProcGenRunAsync` 교체. 레거시 메서드는 **삭제 않고 휴면**(미사용 — 후속 정리 대상).

## 4.6 PR2–PR4 구현 메모 (2026-06-06, 컴파일 0 에러)

**PR2 — 로드아웃 완전 복원** (구현)
- 무기 현재 슬롯: `RunProgressManager.ApplyExtendedFields`에서 라이브 `Player.WeaponManager.CurrentSlotIndex` 우선 캡처(첫 방 -1 해결). 복원은 `AppBootstrapper.RequestRestoreRunAsync`가 `save.weaponCurrentSlot`로 `SaveWeaponSlots(..., curSlot)` (하드코딩 0 제거) → `SpawnPlayerAsync`가 `SwitchToSlotAsync`로 적용.
- 무기 양손(slot0/1): 이미 PR1 경로(weapon0/1PrefabKey→Loadout→SavedWeaponSlots→AcquireWeaponAsync)로 복원됨 — 무변경.
- Relic: `SpawnPlayerAsync`의 `ResolveRelicForDirectSpawn()`=`Loadout?.Relic`. PR1의 `relicKey`→`Loadout.SetRelic`로 데이터 정확히 실림 — 적용 경로 무변경(휴면이라도 정확).
- 무기 강화/업그레이드 별도 런 상태 **없음**(무기=SO + 서버 CSV override로 스탯 재유도). 따라서 SO 키 복원만으로 충분.

**PR3 — 룬 Shape 재구성** (구현, 방어적)
- 발견: 룬 시너지는 **전부 cluster/threshold(점유 기반, `OnZoneCellsUpdated`)**. grid-fill(`HandleGridFilled`)은 hex 보드에서 전체 채움+zone_id 매핑 실패로 실질 비활성 → **PR1 점유 복원이 이미 시너지 완전**.
- PR3 추가분 = **재편집 가능 Shape 객체** 뿐(시너지 정합성은 무관). 점유 셀(`runeCellsJson`)이 시너지 권위, Shape 배치(`runePlacementsJson`)는 시각/상호작용 레이어.
- `BoardManager.CapturePlacements/RestorePlacedShape`(드래그 기하 비의존, 셀 직접 점유), `MerlinRuneBridge.CaptureRunePlacements/RestoreRunePlacements`(instanceId로 인벤토리 재바인딩 → 재집기 동기화). 복원은 `try/catch`로 감싸 실패 시 점유 폴백(빌드 효과 보존).

**PR4 — Steam Cloud** (코드 0 = Auto-Cloud 채택, 회사/제품명 변경 보류)
- Steamworks.NET 통합·`SteamAPI.Init()`(`SteamManager`) 호출됨 → Auto-Cloud 사용 시 **앱 코드 추가 0**. 로컬 파일을 쓰기만 하면 Steam 클라가 앱 시작/종료에 동기화.
- **파트너 사이트(Steamworks) Cloud 설정 — 등록할 항목**:
  - Cloud 켜기 + Byte/File 쿼터 지정.
  - Local Save 매핑(Auto-Cloud):
    - Windows: Root = `WinAppDataLocalLow`, Subdir = `{CompanyName}/{ProductName}` (현재 `DefaultCompany/Project_A`), Pattern = `run_save.json`, `run_save.bak` (메타 추가 시 `meta_save.json`, `meta_save.bak`).
    - macOS/Linux: persistentDataPath 루트가 다름(`~/Library/Application Support/...`, `~/.config/unity3d/...`) → 출시 플랫폼별 Root 변수 별도 등록.
  - Recursive = 불필요(루트 직속 파일).
- **보류(코드 미변경)**: 회사/제품명 `DefaultCompany/Project_A`는 출시 직전 확정. 변경 시 LocalLow 하위 경로가 바뀌므로 파트너 Cloud 패턴 경로도 갱신 필요(코드 영향 없음).
- ISteamRemoteStorage 명시 동기화는 **미채택**(Auto-Cloud로 충분, 이중 동기화 회피 — 멀티기기 충돌 정밀 제어가 필요해지면 그때 도입).

## 4.7 PR5–PR6 구현 메모 (2026-06-06, 컴파일 0 에러)

**PR5 — 뒤끝 텔레메트리 분리 + 메타 로컬화 (보수적 이중 기록)**
- 위험 회피: 작동 중인 각성/통화를 깨지 않기 위해 **로컬 권위 + 뒤끝 병행(이중 기록)** 채택. 모든 메타 읽기(`PlayerRuntimeStats.RefreshAwakening`, 통화 UI)는 `BackendGameData.Data`를 그대로 보고 — **무변경**.
- `LocalFileMetaStore`(meta_save.json, 원자적+.bak, JsonUtility<UserGameData>).
- `BackendGameData.LoadAsync`: 서버 로드 후 **로컬 있으면 우선 적용(오프라인/이어쓰기), 없으면 서버값을 로컬로 이관(최초 1회 마이그레이션)**. `SaveAsync`: 로컬 먼저 저장(오프라인 안전) + 기존 뒤끝 UpdateV2 병행. `ApplyRunResultAsync`는 `SaveAsync` 경유로 자동 이중 기록.
- 안전망: 로컬 손상/부재 → 서버값 사용(기존 동작). 메타 손실 불가.
- **보류/미완**: (a) 완전 fire-and-forget 텔레메트리 분리는 미적용 — 뒤끝 저장이 여전히 `SaveAsync` 내 await(단 호출부 `HandleRunEndedAsync`가 UniTaskVoid라 게임 비차단). (b) 멀티기기: 로컬 우선이라 다른 기기의 더 새로운 서버값이 무시될 수 있음(단일기기 개발 가정). 출시 전 세대/타임스탬프 비교 필요. (c) `CheckpointZone`/`ClearRewardTrigger`의 서버 `RunProgressManager.SaveAsync`는 **절차방에 미부착(휴면)** — 미수정(외과적). 절차 reward 룸에 ClearRewardTrigger가 토큰으로 들어가면 휴면 서버 기록만 발생(로컬 권위와 무충돌).

**PR6 — 결정성 강화 (방 재생성 한정)**
- 핵심 통찰: **저장은 방 입구(전투 전)**라서 방의 post-entry 콘텐츠(보상/상점/버프 롤)는 세이브에 없고 이어하기 시 양쪽 타임라인 모두 새로 롤 → 재현 불필요. 재현해야 할 건 **방 자체**.
- 방 지오메트리는 이미 결정적(PR1: currentRoomPoolKey/mirror/heading 저장). 남은 비결정 = `ApplyMonsterSpawnerPlan`의 Fisher-Yates(`UnityEngine.Random`) → 같은 방이 다른 스포너로 빌드됨.
- 수정: `ApplyMonsterSpawnerPlan(grid, max, System.Random rng=null)`, `BuildProcRoomAsync(..., System.Random roomRng=null)`, `RunFlowController.EnterRoomAsync`가 `roomRng = new Random(Combine(masterSeed, visitCount))` 주입 → **이어하기 시 방+스포너 완전 재현**. 레거시 호출부는 rng=null(전역 Random) 무변경.
- **의도적 미변경(연출/비세이브)**: `BlockPalette` 블록 변종(L73), `MapBuilder` 데코 회전(L592) = 시각. 상점 카탈로그/보상/버프 롤 = post-entry, 세이브 외 → 과설계 회피로 미교체.

## 5. 선결/확인 필요

1. **복원 시점 정책 A vs B** (§2.5) — 방 진입 직후 전투재시작(권장) vs 클리어 후 출구선택.
2. **결정성 범위** — 방 토폴로지/출구만(부분, MVP) vs 보상·상점·버프까지(PR6).
3. **룬 보드 복원 우선순위** — PR3를 MVP 포함할지(퍼즐 진행 손실 허용 여부).
4. **Steam Cloud 방식** — Auto-Cloud(코드0, MVP) vs ISteamRemoteStorage(정밀). 회사/제품명(`DefaultCompany/Project_A`) 출시 전 확정.
5. **확인 필요(미정독)**: `GridSquare` 행/열 좌표 노출 여부(PR3 스키마 좌우), `zone-layout` 복원 경로가 절차 외 다른 곳에서 쓰이는지(폐기 안전성), 메타를 로컬로 옮길 때 뒤끝 랭킹이 요구하는 최소 서버 필드 집합.
