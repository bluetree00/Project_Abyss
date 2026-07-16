# 런 내 보상·특수방 배치 현황 (조사 자료)

- 작성일: 2026-07-04
- 목적: 런 안에서 **이벤트방 / 모루(무기 강화) / 서약 / 아이템**이 현재 어떤 타이밍·확률·위치로 제공되는지의 코드/데이터 정본 그림. 이후 배치 타이밍 기획의 기반 자료.
- 범위: **현황 조사만**. 기획안·수정 제안 아님. 모든 수치는 근거(file:line / .asset YAML / CSV)와 함께 표기하며, 불확실한 것은 "⚠️ 확인 필요"로 명시.
- 조사 방식: 코드/에셋 직접 판독 + 영역별 병렬 조사(이벤트방/런구조/모루/서약/아이템).

---

## 0. 핵심 요약 (한눈에)

| 요소 | 배치 방식 | 타이밍/위치 | 빈도·상한 | 상태 |
|---|---|---|---|---|
| **런 구조/방 추첨** | `RunSequencer` + `IRunStructure`(CSV 정본 → SO 폴백) | 매 방 클리어 시 출구 종류 롤 | 확률형 + 마일스톤 강제 + 보스 임계 | 배선 완료, **CSV↔SO 값 불일치** |
| **이벤트방** | 방 풀 `Event` 카테고리 + 확률/마일스톤 | 확률 20%, 캡 2/챕터 | Ch1만 템플릿 1개, Ch2~4 = 0개 | **부분 배선**(콘텐츠·컨트롤러 미구현) |
| **모루(WeaponForgeAltar)** | BaseCamp 씬 손배치 프리팹 | 베이스캠프 전용, 런당 1회 | 소비 시 파괴, 던전 재등장 없음 | 배선 완료(허브 한정) |
| **서약(Covenant)** | `CV` 토큰 → `WorldCovenantPickup` 3지선다 | Event방 CV 토큰 위치 | 12종 풀 3택, 보유 상한 4 | 시스템 완성, **배치처는 Ch1 1곳뿐** |
| **아이템** | 방클리어 보상 / 상점 / (월드픽업/이벤트) | 매 방 클리어 + 상점방 | 방당 1개(드롭 100% 강제) | 방클리어·상점 완비, 이벤트보상 미배선 |

**전체 그림 한 줄:** 골격(시퀀서·확률·마일스톤·보스 임계)과 아이템 페이싱(방클리어 100% + 상점)은 배선 완료. 반면 **이벤트방·서약의 실제 배치처는 Chapter 1의 방 1개(`ch1_event_sanctum_01`)에 사실상 집중**돼 있고 Ch2~4는 비어 있음. 모루는 허브 전용으로 런 중 반복 요소가 아님.

---

## 1. 런 구조 / 방 추첨

### 1-1. 핵심 클래스·데이터 흐름

- `RunSequencer` — 절차적 방 진행 엔진. 매 클리어마다 출구 문 종류를 롤.
  `Assets/RelicFairy/Systems/Stage/MapGen/RunSequencer.cs:58-287`
- `RunStructureConfig` (ScriptableObject, 오프라인 폴백) — 확률/마일스톤/난이도 곡선 보유.
  `Assets/RelicFairy/Systems/Stage/MapGen/RunStructureConfig.cs:29-78`
- `RunStructureEntry` (CSV 파싱, `IRunStructure` 구현) — CDN 정본.
  `Assets/RelicFairy/Systems/Managers/Scripts/DataManagers/RunStructureDataManager.cs:119-211`

**출구 종류 enum** (`RunSequencer.cs:6-14`), 마일스톤 `kind` 숫자 해석의 근거:

| enum | 값 |
|---|---|
| Normal | 0 |
| Elite | 1 |
| Shop | 2 |
| Event | 3 |
| PreBoss | 4 |
| Boss | 5 |

### 1-2. Config 해석 우선순위 (어떤 값이 실제로 적용되는가)

`RunFlowController.EnsureStructureConfigAsync()` — `RunFlowController.cs:215-250`

| 순위 | 소스 | 조건 | 근거 |
|---|---|---|---|
| 0 | **인스펙터 직접 배선 SO** (`_structureConfig`) | 배선돼 있으면 무조건 우선 | `RunFlowController.cs:220-225` |
| 1 | **CSV 정본** (CDN `RUN_STRUCTURE`, chapter_id 조회) | 서버에 엔트리 존재 시 | `:228-233`, `RunStructureDataManager.cs:39` |
| 2 | **SO 폴백** (Addressables: `CHAPTER_N_RUN_STRUCTURE` → `RUN_STRUCTURE_DEFAULT`) | CSV 없을 때 | `:235-244`, 기본 키 `_structureConfigKey = "RUN_STRUCTURE_DEFAULT"` (`:20`) |
| 3 | **null → 전 방 Normal** (구조 비활성, 진행은 막지 않음) | 위 전부 실패 | `:246-247` |

> ⚠️ **확인 필요:** 위 순위상 실제 적용값은 "CDN에 RUN_STRUCTURE가 업로드됐는지"에 좌우된다. 프로젝트 내 `RUN_STRUCTURE.csv`는 **없고**(CDN 로드 후 `persistentDataPath/run_structure.json` 캐시), 원본 CSV는 데스크탑 `C:\Users\u\Desktop\사용중 테이블\RUN_STRUCTURE.csv`에만 존재. CDN 업로드 여부 미확인 → **업로드 안 됐으면 아래 SO 폴백값이 실동작값**이다.

### 1-3. 실측값 ① — SO 에셋 (오프라인 폴백/개발 기본값)

`Assets/RelicFairy/Systems/Stage/MapGen/Data/RunStructure_*.asset`

| 에셋 | bossThreshold | shopChance/cap | eventChance/cap | eliteChance | 마일스톤(visitIndex:kind) |
|---|---|---|---|---|---|
| `RunStructure_Default` | 4 | 0.25 / 2 | 0.2 / 2 | 0.15 | 3:Shop, 5:Event, 7:Elite |
| `RunStructure_Ch1` | **1** | 0.25 / 2 | 0.2 / 2 | 0.15 | **1:Shop** |
| `RunStructure_Ch2` | 4 | 0.25 / 2 | 0.2 / 2 | 0.15 | 3:Shop, 5:Event, 7:Elite |
| `RunStructure_Ch3` | 4 | 0.25 / 2 | 0.2 / 2 | 0.15 | 3:Shop, 5:Event, 7:Elite |
| `RunStructure_Ch4` | 4 | 0.25 / 2 | 0.2 / 2 | 0.15 | 3:Shop, 5:Event, 7:Elite |

- 난이도 곡선(전 에셋 공통): 선형 `visit 0 → 0.4`, `visit 12 → 1.8` (`*.asset` `_difficultyByVisit`)
- 근거: `RunStructure_Default.asset:15-53`, `RunStructure_Ch1.asset:15-49`, `RunStructure_Ch2~4.asset:15-53`

### 1-4. 실측값 ② — 데스크탑 CSV (CDN 업로드 시 정본이 될 값)

`C:\Users\u\Desktop\사용중 테이블\RUN_STRUCTURE.csv` (행 2~5, 조사 인용)

| Chapter | shopChance/cap | eventChance/cap | eliteChance | boss_threshold | 마일스톤 |
|---|---|---|---|---|---|
| Chapter1 | 0.25 / 2 | 0.2 / 2 | 0.15 | 8 | 4:Shop, 6:Event |
| Chapter2 | 0.25 / 2 | 0.2 / 2 | 0.18 | 10 | 4:Shop, 7:Event, 9:Elite |
| Chapter3 | 0.25 / 2 | 0.2 / 2 | 0.2 | 10 | 4:Shop, 7:Event, 9:Elite |
| Chapter4 | 0.25 / 2 | 0.2 / 2 | 0.22 | 12 | 4:Shop, 7:Event, 10:Elite |

> **CSV↔SO 불일치:** 보스 임계(SO Ch1=1, Ch2~4=4 vs CSV 8/10/10/12)와 마일스톤 위치가 서로 다르다. SO(특히 Ch1=1)는 **초단축 데모/테스트 구성**으로 보이며, CSV가 의도된 길이. 어느 것이 실동작인지는 §1-2의 CDN 업로드 여부에 달림.

### 1-5. 방 추첨 알고리즘

1. **런 시작 시 1회** `BuildPlan()` — 마스터 시드+config로 보스 도달까지 전 깊이 출구 종류를 미리 산출(경로 독립). `RunSequencer.cs:115-128`
2. **매 방 클리어 시** `RollExits()` — 출구 문 1~2개 계획 반환. `RunSequencer.cs:131-171`
   - 일반 페이즈: **2슬롯(직진/턴)**. 한 문쌍에서 **특수방(Shop/Event)은 최대 1개**(`specialUsed` 플래그, `:164-169`).
   - 마일스톤 강제 시: **두 출구 모두 해당 종류로 확정**(선택지 아님, `:167`).
   - 확률 롤 순서(`RollKind`, `:217-231`): Shop(cap·확률) → Event(cap·확률) → Elite(확률) → 아니면 Normal.
   - 보스 게이팅: `_visitCount >= BossThreshold` → PreBoss → Boss(`:145-151, 140-144`).
3. **템플릿 선택** `PickEntry()` — 카테고리 필터 → 쿨다운 미적용 우선 → 난이도 윈도(±0.35) → RNG. `RunSequencer.cs:233-249`
   - 쿨다운 = `max(2, 카테고리 풀 크기 × 0.4)` 턴. `:273-279`
4. **폴백**: 해당 종류 템플릿이 풀에 없으면 **Normal로 강등**(+경고 로그). `MakeDoor`, `RunSequencer.cs:190-200`

**챕터당 방 개수** ≈ `BossThreshold + 2`(PreBoss 1 + Boss 1). 즉 SO Ch2~4 = ~6방, CSV Ch2/3 = ~12방.

### 1-6. 방 풀(카테고리별 템플릿)

- 풀 엔트리: `ZonePoolEntry`(pool_key/category/size_tag/grid_csv/difficulty_scale). 카테고리 = Normal/Elite/Shop/Event/PreBoss/Boss.
- 로드: `Managers.ZoneLayout.LoadPoolAsync(poolKey)`, 키 규칙 `CHAPTER_{N}_ROOM_POOL`. `RunFlowController.cs:87-93`
- 룸 풀 CSV 위치: `Assets/RelicFairy/Docs/CHAPTER_1_ROOM_POOL.csv` 등.
- Boss/PreBoss는 풀마다 1개라 카테고리 폴백이 결정적. `RunSequencer.cs:209-215`

---

## 2. 이벤트방 현황

**상태: 부분 배선 — 시스템은 완성, 콘텐츠/템플릿이 사실상 없음.**

| 항목 | 상태 | 근거 |
|---|---|---|
| 카테고리 정의 | ✅ | `RoomPlanKind.Event` (`RunSequencer.cs:11`) |
| 배치 규칙 | ✅ | eventChance 0.2, cap 2/챕터, 마일스톤 지원 (`RunStructureConfig.cs:44-45`) |
| 실행/폴백 로직 | ✅ | `MakeDoor→PickEntry→Normal 폴백` (`RunSequencer.cs:190-200`) |
| **템플릿(방)** | ⚠️ **Ch1만 1개, Ch2~4 = 0개** | `CHAPTER_1_ROOM_POOL.csv:36` = `ch1_event_sanctum_01`(22×22) |
| **방 컨트롤러/콘텐츠** | ❌ 미구현 | `EventStageData.cs`는 **참조 0곳**(enum Money/Item/Monster/GangHwa + 대부분 주석), 스텁 |

- Ch2~4는 Event 템플릿이 없어 Event 롤 시 `PickEntry`가 null → **Normal로 강등**(경고 로그). 기능상 소프트락은 없지만 이벤트방은 나오지 않음.
- Ch1의 `ch1_event_sanctum_01`은 맵 데이터만 있고 `ShopRoomController` 같은 전용 컨트롤러가 없어 **빈 방일 가능성 높음**(단, 이 방에 서약 `CV` 토큰이 들어있음 — §4 참조).
- 파일: `Assets/RelicFairy/Systems/Stage/Data/EventStageData.cs` (스텁)

---

## 3. 모루 (WeaponForgeAltar) — 무기 강화/선택

**상태: 배선 완료. 단, 베이스캠프(허브) 전용 · 런당 1회 · 절차생성 미포함.**

| 항목 | 내용 | 근거 |
|---|---|---|
| 제공 내용 | **근접 1종(대검/카타나) + 원거리 1종(보우/석궁) 동시 장착** | `WeaponForgeAltar.cs:7-11, 19-23, 131-149`; `UI_WeaponForgePopup.cs:136-138, 207-210` |
| 배치 | **BaseCamp.unity 씬 손배치 프리팹** (PrefabInstance 341071736) | `WeaponForgeAltar.prefab`, `BaseCamp.unity`(~41858행); `docs/basecamp-design.md:181,271` |
| 절차생성 배치 | ❌ 없음 — MapGen **토큰 핸들러에 모루 없음** | MapGen 하위 "WeaponForge" 참조 0개 |
| 빈도 | **런당 1회**, 베이스캠프 진입 시 항상 접근 | — |
| 재등장 | ❌ 확정 시 `_claimed=true` + `Destroy`, 던전 진입 후 씬 언로드 | `WeaponForgeAltar.cs:53, 114-118` |
| 게이팅 | `Loadout.IsReady`(캐릭터 선택 선행), 팝업 `BlocksGameplay=true`(시간정지·입력잠금) | `WeaponForgeAltar.cs:83-88`; `UI_WeaponForgePopup.cs:13` |

- 제공 = **무기 강화가 아니라 무기 세트 선택**(근접+원거리 택1씩 동시 장착). 슬롯0(근접·활성)/슬롯1(원거리).
- ⚠️ **참고(별개 경로):** MapGen 토큰에 `WP = WeaponPickupHandler`가 별도로 존재한다 → 모루와 무관한 **런 중 무기 픽업** 경로가 토큰으로 배치될 수 있음. (본 조사 범위 밖, 배치 현황은 추가 확인 필요.)

---

## 4. 서약 (Covenant) — 제단/획득

**상태: 시스템 완성. 단, 런 중 실제 배치처는 Chapter 1의 1곳뿐.**

### 4-1. 배치 경로

- 토큰 `CV` (TokenCategory.Special) → `CovenantAltarHandler.Execute()` → `WorldCovenantPickup` 스폰 → F 상호작용 시 3지선다.
  `Assets/RelicFairy/Systems/Stage/MapGen/Token/Handlers/Special/CovenantAltarHandler.cs:8-18`
- **현재 CV 토큰 배치:** Chapter 1의 `ch1_event_sanctum_01` **1곳**. Ch2~4 ROOM_POOL에는 `CV` 토큰 **없음**.

### 4-2. 3지선다 롤

- 후보 **3개 고정**, 풀 **12종**(Nimue/Morgana/Lionel/Elaine/Kay/Bedivere/Tristan/Isolde/Leodegrance/Guinevere/Arthur/Galahad).
  `WorldCovenantPickup.cs:94-102`, `CovenantFactory.cs:24-43`(주석 40: "12종이 랜덤 3지선다 풀")
- 중복 배제: 런 중 획득은 `CovenantHandler.Has()`로 보유분 제외(`CovenantPickup.cs:99`), 베이스캠프는 `ReservedCovenants` 제외(`:82`). 카테고리 제약 없음(균등).
- **보유 상한 = 4** (`CovenantHandler.cs:13` `MaxCovenants=4`).

### 4-3. 시작 서약 vs 런 중 획득

- **자동 부여 시작 서약: 없음** — `GameRunSession` BindPlayer는 `CovenantHandler.Initialize`만, 서약 초기 부여 로직 없음.
- **베이스캠프 선택분:** `CovenantPickup` → `PlayerLoadout.ReservedCovenants` → 던전 진입 시 `StartRoomGate.ExitStartRoomAsync()`에서 `TryAdd`(`StartRoomGate.cs:334-338`). ⚠️ **확인 필요:** 베이스캠프 씬에 `CovenantPickup` 오브젝트가 실제 배치돼 있는지 미확인(클래스는 존재).
- **런 중 획득:** Event방 CV 토큰 → `WorldCovenantPickup.OpenChoiceAsync` → `TryAdd`.

### 4-4. 데이터 소스

- `CovenantDataTableSO`(SO 직렬화) / CDN `COVENANT_STAT_DATA` / Addressables 폴백 3단.
  `CovenantDataManager.cs:16, 28-48`
- 테스트 트리거: `CovenantChoiceTestTrigger.cs:7-26`(T키 스폰, 개발용) — 실배선과 별개.

---

## 5. 아이템 제공 경로 (전수)

### 5-1. 경로별 배선 상태

| 경로 | 상태 | 타이밍 | 개수 | 등급 롤 | 근거 |
|---|---|---|---|---|---|
| **방 클리어 보상** | ✅ 완비 | 모든 방 클리어 | 1개(+보스방 보너스) | Luck 기반 | `RoomClearGate.cs:80,101-151` |
| **상점 구매** | ✅ 완비 | 상점방 진입 | 기본 3슬롯 | Luck 기반 | `ShopRoomController.cs:89-118,391-408` |
| **월드 픽업** | ⚠️ 부분 | 불명 | 불명 | 불명 | `WorldItemDisplay.cs:132-162`(픽업 UI만, 드롭 스폰원 = RoomClearGate뿐) |
| **이벤트 보상** | ❌ 미배선 | — | 0 | — | 아이템 이벤트 보상 구조 없음 |

### 5-2. 방 클리어 보상 상세

- 매 방 클리어 시 `RollRewardItem()` → 보상 있으면 `SpawnRewardObject()`(ClearRewardTrigger 부착) → 픽업 시 `ItemInventory.AddToStaging`.
  `RoomClearGate.cs:68-151`, `ClearRewardTrigger.cs:105-196`
- **드롭 확률: 현재 100% 강제** — `private const bool ForceDropAlways = true;` `RoomClearGate.cs:20`(주석: "추후 `LuckRollService.TryRollDrop`으로 복구할 때 false로"). 원래 테이블 dropChance=0.6.
- 보스방 보너스: `EffectManager.GetBonusBossDropCount()`로 추가 롤. `RoomClearGate.cs:84-93`
- Luck→등급: `LuckRollTableSO`(7단계, `:68-91`). 예: Luck 0~10 = Common 100%; 85~100 = Common 9/Rare 17/Epic 34/Legendary 40%.

### 5-3. 상점 상세

- 슬롯 기본 3개(`shopSlotCount`), 리롤 기본 off(`shopRerollEnabled=false`). `ShopRoomController.cs:92,138-157`
- 차트 기반 진열(ITEM_DATA/WEAPON_DATA 가중 추출, 결정적 RoomRng, 중복·보유분 제외). `:282-315`
- 가격표(`ShopPriceTableSO.cs:14-24`): 아이템 C50/R150/E400/L1000, 무기 C100/R300/E700/L1500.
- 등급 롤: `TryRollEntry`(Luck, 동일 LuckRollTable). `:301,391-408`

### 5-4. 데이터 소스

- `ItemDataManager` 3단: CDN `ITEM_DATA` → `persistentDataPath/item_data.json` → Addressables 폴백. `ItemDataManager.cs:26-61`

### 5-5. 런 전체 아이템 획득량 (추정)

> 아래는 **조사 추정치**(월드픽업·보스보너스·상점 선택률 미확정). SO/CSV 어느 config가 실동작인지에 따라 방 개수가 달라짐.

- 방 클리어 보상은 **매 방 1개 100% 강제** → 챕터 방 개수에 직결.
  - SO Ch2~4(≈6방): ~4~5개
  - CSV Ch2/3(≈12방): ~10~11개 + 보스보너스
- 상점: 챕터당 1~2회 × 3슬롯 중 골드 여유만큼 구매.
- 이벤트 보상 = 0(미배선).

---

## 6. 대표 타임라인

> 아래는 **SO 폴백값 기준**(CDN 미업로드 가정, §1-2·§1-4). CSV가 업로드돼 있으면 CSV 마일스톤/보스 임계로 바뀐다. `visitN`은 진입 순번.

### 6-1. SO 폴백 기준

- **Chapter 1 (SO, bossThreshold=1, ms 1:Shop):**
  `Start → Shop(visit1) → PreBoss(visit2) → Boss(visit3)` — **초단축 데모 구성(4방)**. 이벤트/엘리트 없음.
- **Chapter 2~4 / Default (SO, bossThreshold=4, ms 3:Shop/5:Event/7:Elite):**
  `Start → [visit1 롤] → [visit2 롤] → Shop(visit3 강제) → [visit4 롤] → PreBoss(visit5) → Boss(visit6)` — **~6방**.
  롤 구간은 확률상 대부분 Normal(가끔 Elite 15%, Shop/Event가 캡·확률로 추가될 수 있음).
  ⚠️ **데드 마일스톤:** ms의 `5:Event`, `7:Elite`는 boss 임계(4) **이후 순번이라 절대 도달 안 됨**(`RollExits`가 visit≥4에서 PreBoss로 전이). 실제로는 `3:Shop`만 유효. (`RunFlowController.LogResolvedStructure`도 `v=1..BossThreshold`만 순회 — `:258`.)

### 6-2. CSV 기준 (업로드 시)

- **Chapter 1** (boss@8, ms 4:Shop/6:Event): `Start → 롤들 → Shop@4 → 롤 → Event@6 → 롤 → PreBoss@8 → Boss@9` (~9~10방).
- **Chapter 2/3** (boss@10, ms 4:Shop/7:Event/9:Elite): ~11~12방.
- **Chapter 4** (boss@12, ms 4:Shop/7:Event/10:Elite): ~13~14방.
- 단, **Event 마일스톤이 유효해도 템플릿이 Ch2~4에 0개**라 실제로는 Normal 폴백(§2).

### 6-3. 각 방에서 나올 수 있는 것 (현행 규칙)

| 방 종류 | 전투 | 아이템(방보상) | 상점 | 이벤트 | 서약 | 모루 |
|---|---|---|---|---|---|---|
| Normal/Elite | ✅ | ✅ 1개 | — | — | — | — |
| Shop | (전투 없음) | 구매 | ✅ 3슬롯 | — | — | — |
| Event(Ch1만) | ? | ? | — | ⚠️ 콘텐츠 미구현 | ✅ CV 토큰(Ch1) | — |
| PreBoss/Boss | ✅ | ✅(보스 보너스) | — | — | — | — |
| BaseCamp(런 밖) | — | — | — | — | 선택(⚠️배치 확인) | ✅ 1회 |

---

## 7. 빈 구멍 / 확인 필요

**빈 구멍(관찰된 사실):**
1. **이벤트방 콘텐츠 부재** — Event 카테고리·확률·마일스톤은 있으나 템플릿은 Ch1 1개(`ch1_event_sanctum_01`), Ch2~4 = 0개. `EventStageData.cs`는 미사용 스텁 → 이벤트방 콘텐츠/컨트롤러 없음.
2. **서약 배치처 협소** — CV 토큰이 Ch1 1곳에만. Ch2~4에서 런 중 서약 획득 경로 없음. 이벤트방 부재와 동일 원인(특수 템플릿 부재)에 종속.
3. **데드 마일스톤(SO)** — Ch2~4/Default의 `5:Event`, `7:Elite` 마일스톤이 boss 임계(4) 이후라 절대 발동 안 됨. `3:Shop`만 유효.
4. **이벤트 보상(아이템) 미배선** — 돌발 아이템 보상 경로 없음.
5. **드롭 확률 임시 100%** — `RoomClearGate.ForceDropAlways=true`(방당 무조건 1개). Luck 기반 드롭 확률(0.6)은 비활성 → Luck은 "드롭 여부"가 아닌 "등급"에만 현재 관여.
6. **모루는 런 중 반복 요소 아님** — 허브 전용 1회. 던전 내 무기 관련 반복은 `WP` 토큰(WeaponPickup)에 의존하나 배치 현황 미확인.

**⚠️ 확인 필요:**
1. **CDN RUN_STRUCTURE 업로드 여부** — 미업로드면 SO 폴백(특히 Ch1=1의 초단축)이 실동작값. 데스크탑 CSV는 프로젝트/서버 반영 전 상태일 수 있음.
2. **CSV↔SO 값 불일치 의도** — boss 임계·마일스톤이 서로 다름. 어느 쪽이 최신 의도인지.
3. **`RunStructure_Ch1~4.asset`의 Addressable 등록/키(`CHAPTER_N_RUN_STRUCTURE`)** — SO 폴백 2순위가 실제로 챕터별 에셋을 잡는지, 아니면 `RUN_STRUCTURE_DEFAULT`만 로드되는지.
4. **베이스캠프 `CovenantPickup` 씬 배치 여부** — 클래스는 있으나 실제 오브젝트 배치 미확인.
5. **월드 자유 드롭(WorldItemDisplay) 스폰원** — 방클리어 외 드롭 생성 지점 미확인.
6. **`WP`(WeaponPickup) 토큰의 챕터별 배치 현황** — 런 중 무기 획득 빈도.
7. **Ch1 `ch1_event_sanctum_01`의 실제 내용** — CV 토큰 외 이벤트 로직/보상이 있는지(빈 방 여부).

---

## 참고: 근거 파일 인덱스

- 런 시퀀서/구조: `RunSequencer.cs`, `RunStructureConfig.cs`, `RunStructureDataManager.cs`, `RunFlowController.cs`, `Data/RunStructure_*.asset`
- 룸 풀: `Assets/RelicFairy/Docs/CHAPTER_1~4_ROOM_POOL.csv`, `ZonePoolEntry.cs`
- 이벤트방: `EventStageData.cs`
- 모루: `StartRoom/WeaponForgeAltar.cs`, `UI/Popup/UI_WeaponForgePopup.cs`, `docs/basecamp-design.md`
- 서약: `Covenant/WorldCovenantPickup.cs`, `Covenant/CovenantPickup.cs`, `Covenant/Core/CovenantFactory.cs`, `Covenant/Core/CovenantHandler.cs`, `Token/Handlers/Special/CovenantAltarHandler.cs`, `CovenantDataManager.cs`
- 아이템: `RoomClearGate.cs`, `ClearRewardTrigger.cs`, `ShopRoomController.cs`, `ShopPriceTableSO.cs`, `LuckRollTableSO.cs`, `WorldItemDisplay.cs`, `ItemDataManager.cs`
- 데스크탑 CSV: `C:\Users\u\Desktop\사용중 테이블\RUN_STRUCTURE.csv`
