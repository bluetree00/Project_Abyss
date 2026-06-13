# 맵 / 방 시스템 통합 가이드

> 맵 생성·등록·팔레트·토큰·방 제작(일반/보스)을 한 곳에 정리한 마스터 문서.
> 세부 명세는 각 절의 링크 문서를 참조. (2026-06-12 통합)
>
> **관련 문서**
> - [procgen-design.md](procgen-design.md) — 절차적 생성 아키텍처(왜 이렇게 설계했나)
> - [MAP_TOKENS.md](../Assets/RelicFairy/Docs/MAP_TOKENS.md) — 그리드 토큰 전체 명세(기획자용)
> - [boss-custom-arena-design.md](boss-custom-arena-design.md) — 보스 커스텀 아레나 설계

---

## 0. 전체 구조 (챕터 → 방 → 빌드)

### 0-1. 챕터 구성 (최상위)

```
┌─────────────────────────────────────────────────────────────────┐
│  ChapterRegistry (SO)                                            │
│   ├ chapters : List<ChapterDataSO>      (챕터별 데이터)          │
│   ├ layouts  : List<ChapterLayoutSO>    (레거시 고정 12방 배치)  │
│   └ _finalChapter : ChapterId           (이 보스 클리어=런 종료) │
└───────────────────────────┬─────────────────────────────────────┘
                            │ GetData(ChapterId)
                            ▼
┌─────────────────────────────────────────────────────────────────┐
│  ChapterDataSO  (챕터 1개)        ◀──── CHAPTER_DATA.csv (서버 O/R)│
│   theme ───────────────▶ BlockPalette / DecorationCatalog (외형·장식)
│   bossSpawnTable ──────▶ MonsterSpawnTableSO ─▶ BossSpawner (챕터 보스)
│   zonePoolKey ─────────▶ CHAPTER_N_ROOM_POOL.csv ─▶ ZonePoolEntry[] (방 풀)
│   difficultyScale / monsterCountScale / monsterPoolTag ▶ 전투 스탯·필터
│   gold/itemMultiplier ─▶ 보상 배수                                 │
│   middleLayers/peakLayer ▶ 노드 트리 형태(층 수)                   │
│   mapBg / bgm / fieldPrefab ▶ 배경·사운드·환경 연출               │
└───────────────────────────┬─────────────────────────────────────┘
              │ ToRuntime() + MergeFromServer(CHAPTER_DATA.csv)
              ▼
        ChapterRuntimeData (SO 기본값 + 서버 병합)  ── zonePoolKey ──┐
                                                                     ▼
                                          RunFlowController.StartRunAsync
```

**챕터를 구성하는 SO 3개**: `ChapterRegistry`(전체 목록·최종 챕터) → `ChapterDataSO`(테마·보스·풀키·난이도) → `ChapterLayoutSO`(레거시 고정 지형).
**챕터→방 연결**은 `ChapterDataSO.zonePoolKey` 한 줄(예: `CHAPTER_1_ROOM_POOL`). `theme`는 CSV가 아니라 SO 인스펙터 값.

| 챕터 | 이름 | zone_pool_key | barrier | monster_pool_tag | 난이도 | 층수 |
|---|---|---|---|---|---|---|
| Chapter1 | 황혼의 숲 | `CHAPTER_1_ROOM_POOL` | Water | forest | 1.0 | 12 (peak 5) |
| Chapter2 | 용암 지대 | `CHAPTER_2_ROOM_POOL` | Lava | lava | 1.5 | 12 (peak 5) |
| Chapter3 | 신성한 성역 | `CHAPTER_3_ROOM_POOL` | Fog | sacred | 2.2 | 12 (peak 5) |
| Chapter4 | 천상의 성채 | `CHAPTER_4_ROOM_POOL` | Void | fortress | 3.0 | 12 (peak 5) |

> 챕터 SO 상세: [ChapterDataSO.cs](../Assets/RelicFairy/Systems/Stage/Stage/ChapterDataSO.cs) · [ChapterRegistry.cs](../Assets/RelicFairy/Systems/Stage/Stage/ChapterRegistry.cs) · 데이터 [CHAPTER_DATA.csv](../Assets/RelicFairy/Docs/CHAPTER_DATA.csv)

### 0-2. 방 빌드 파이프라인 (챕터 풀 → 한 방)

```
CHAPTER_N_ROOM_POOL.csv (방 풀)         RunStructure_Default.asset (런 구조 SO)
        │ 파싱                                  │
        ▼                                       ▼
  ZonePoolEntry[]  ──────────▶  RunSequencer  ◀─┘   (visitCount마다 출구 종류 롤)
        │                          │ DoorPlan(kind, entry)
        │                          ▼
        │                   RunFlowController.EnterRoomAsync
        │                          │ (화면 가림 + 리프프로그 앵커)
        ▼                          ▼
   grid_csv ─────▶  GameRunBootstrapper.BuildProcRoomAsync
                          │
              ┌───────────┴────────────┐
              │ arena_template_key 有?  │
       NO ◀───┤        (옵트인)         ├───▶ YES
       │      └────────────────────────┘     │
       ▼                                       ▼
  격자 빌드 경로                        커스텀 아레나 경로
  MapBuilder(팔레트) + TokenParser      손맵 프리팹 1개 인스턴스화
  (Floor/Wall/장식/스포너/문)          (격자·토큰 스킵, 프리팹이 전부 소유)
              └───────────┬────────────┘
                          ▼
              NavMesh 베이크 → RoomClearController 부착(스포너 자동 수집)
              → 플레이어 배치(입구) → 디졸브 등장 → 출구 게이트
```

**핵심 전환**: 고정 슬롯맵 폐기 → **방 풀 + 런타임 시퀀서 + 격리형 방 전환**(하데스형). 항상 현재 방(+준비 중 다음 방)만 상주. 자세한 근거는 [procgen-design.md](procgen-design.md).

---

## 1. 데이터 & 등록 방식

맵 시스템은 **SO 레지스트리가 아니라 CSV(데이터) + Addressable(주소)** 로 묶인다.

### 1-1. 방 풀 CSV — `CHAPTER_N_ROOM_POOL.csv`
- 한 행 = 방 1개 → `ZonePoolEntry`(plain C# 클래스, SO 아님)로 파싱.
- 원본: 바탕화면 `사용중 테이블/` / in-project 사본: [Assets/RelicFairy/Docs/](../Assets/RelicFairy/Docs/)
- 로딩: [ZoneLayoutManager.LoadPoolAsync](../Assets/RelicFairy/Systems/Stage/MapGen/ZoneLayoutManager.cs#L84) — **서버(뒤끝 CDN) 우선 → 없으면 Addressable CSV 폴백**.
- 컬럼: `pool_key, category, grid_width, grid_height, grid_csv, spawn_local_x/z, theme, palette, difficulty_scale, max_active_spawners, stat_version, arena_template_key`

### 1-2. 런 구조 SO — `RunStructure_Default.asset`
- 유일한 SO. Addressable 키 `RUN_STRUCTURE_DEFAULT`, **전 챕터 공유**.
- [RunStructureConfig](../Assets/RelicFairy/Systems/Stage/MapGen/RunStructureConfig.cs): `_bossThreshold`(보스 진입 visitCount), `_preBossRoomKey`/`_bossRoomKey`(방 pool_key), 상점/이벤트/정예 확률·캡, 난이도 곡선, 확정 마일스톤.

### 1-3. Addressable 그룹 (주소 부여)
| 그룹 .asset | 담는 것 | 예 |
|---|---|---|
| `ChartData` | 방 풀/존 CSV (TextAsset) | `CHAPTER_1_ROOM_POOL` |
| `StageData` | 런 구조 SO 등 | `RUN_STRUCTURE_DEFAULT` |
| `StagePrefabs` | 스테이지/아레나 프리팹 | `Arena_Boss_Ch1`, `Room_Start_Campfire` |
| `Monsters` | 몬스터/보스 프리팹 | (보스 스폰 테이블이 키로 참조) |

> ⚠️ Addressable 등록은 그룹 `.asset` YAML 직접 편집 + force refresh로 한다(MCP execute_code 불가). 그룹 엔트리 = `{m_GUID, m_Address}`.

---

## 2. 방 모델 (1 입구 / 2 출구)

| 방 | 입구 | 출구 | 비고 |
|---|---|---|---|
| 시작 | 0 | N | `FindStartEntry`(Normal 첫 항목/지정 키) |
| 일반·정예 | 1 | 2 (직진+턴) | 특수방은 한 문쌍 최대 1개 |
| 상점·이벤트 | 1 | 1~2 | |
| 보스 전방(PreBoss) | 1 | 1 (→보스 확정) | |
| 보스 | 1 | 0 (종단, 런 종료) | |

- 캐논 로컬: **입구=아래(South) / 직진=위(North) / 턴=옆(좌·우, 미러로 결정)**.
- 회전·미러는 **grid_csv 문자열 변환**(열 반전/90° 회전)으로 처리 — 트랜스폼 회전 안 함([GridTransform], `quarterTurns`).
- 시퀀서 규칙: `visitCount >= bossThreshold`면 PreBoss→Boss 게이팅. 그 외엔 상점/이벤트(캡+확률)/정예(확률)/Normal 롤 + 난이도 윈도·쿨다운으로 pool_key 선택. 상세 [RunSequencer.cs](../Assets/RelicFairy/Systems/Stage/MapGen/RunSequencer.cs).

---

## 3. 그리드 토큰 방식

`grid_csv` 한 셀 = 1 토큰 → [MapDataLoader.Parse](../Assets/RelicFairy/Systems/Stage/MapGen/MapDataLoader.cs)가 `TileType[,]` + 스폰/문/장식 메타로 분해.
빌드는 2단계: **MapBuilder**(지형 블록) + **TokenParser**(스포너·장식·기능 오브젝트).

### 토큰 요약 (전체는 [MAP_TOKENS.md](../Assets/RelicFairy/Docs/MAP_TOKENS.md))
| 분류 | 토큰 | 의미 |
|---|---|---|
| 지형 | `F` `W` `O` `.` | Floor / Wall / Obstacle / 구멍 |
| 기능 | `P` `B` `Sw` `Si` `N` `C` `R` `D` `CV` `SG` | 플레이어스폰 / 보스스폰 / 상점 / NPC / 상자 / 버프 / 서약제단 / 스타트게이트 |
| 문 | `DR<width>` | 외곽 링에만. 선택된 문만 폭만큼 Floor 개방 |
| 스포너 | `[M\|m]([c\|r\|e]<n>)+` | M=확정 / m=후보(max_active_spawners), 등급 c/r/e, 수량 n, 다세그=웨이브 |
| 장식 | `d<code>` | 바닥 깔고 후처리 스폰. 테마 `DecorationCatalog`에 등록된 code만 |

### 토큰 핸들러 (확장 지점)
- [TokenParser](../Assets/RelicFairy/Systems/Stage/MapGen/Token/TokenParser.cs) + [TokenRegistry](../Assets/RelicFairy/Systems/Stage/MapGen/Token/TokenRegistry.cs): `[TokenHandler("X", …)]` 어트리뷰트로 핸들러 자동 등록.
- **Phase 2단계**: `PreBuild`(스포너 — NavMesh 전) → NavMesh 베이크 → `PostBuild`(장식·보스 등).
- 핸들러 예: `BossSpawnHandler(B)`, `MonsterSpawnHandler(M/m)`, `DecorationHandler(d…)`, `CovenantAltarHandler(CV)`, `CharacterPickupHandler(CP)`, `WeaponPickupHandler(WP)`.
- 검증: 메뉴 `Tools/RelicFairy/Validate Room CSVs`(린터) + `Validate Token Canon`(드리프트).

---

## 4. 팔레트 방식 (블록 외형)

방 지형의 **외형**은 [BlockPalette](../Assets/RelicFairy/Systems/Stage/MapGen/BlockPalette.cs) SO가 결정.

- **테마 매칭**: 방 `theme`(챕터 테마로 오버라이드) ↔ `BlockPalette.themeMatch`. 실패 시 `themeMatch="*"`(Default)로 폴백.
- **구조**: `TileType → List<BlockDef>` 매핑. 같은 TileType에 여러 BlockDef 등록 시 **가중치 랜덤**. `Pick(TileType)`으로 선택.
- **공용 블록**: `BD_MonsterSpawn/PlayerSpawn/BossSpawn/ShopStall`은 모든 팔레트 공유. 테마 전용은 `BD_Floor/Wall/Obstacle_<Theme>`.
- **조명**: 팔레트의 `RoomLightingConfig`(벽등/중앙등 프리팹·간격)로 `MapBuilder.BuildRoomLights`.
- `BD_<Name>.asset`의 `tileType` 정수는 `TileType` enum과 일치해야 함(0=Floor … 6=BossSpawn …). 표는 [Data/README.md](../Assets/RelicFairy/Systems/Stage/MapGen/Data/README.md).

| 테마 | 팔레트 | 상태 |
|---|---|---|
| Forest | `BlockPalette_Forest` | 전용 외형 + 장식 ✓ |
| 그 외 | `BlockPalette_Default` | 중립 외형 + 장식 없음(폴백) |

> 새 테마 추가 절차(BD 3종 → 팔레트 → 카탈로그 → 부트스트래퍼 등록)는 [Data/README.md](../Assets/RelicFairy/Systems/Stage/MapGen/Data/README.md) 참조.

---

## 5. 빌드 흐름 — 격자 vs 커스텀 아레나

[GameRunBootstrapper.BuildProcRoomAsync](../Assets/RelicFairy/Systems/Bootstrapper/Scripts/GameRunBootstrapper.cs#L854)가 분기한다:

```
useCustomArena = !string.IsNullOrEmpty(entry.arena_template_key)
```

- **키 없음 → 격자 빌드**: `MapBuilder.Build/Ceiling/Lights`(팔레트) + 문 복도 + `TokenParser`(Pre/PostBuild).
- **키 있음 → 커스텀 아레나**: 격자 지형·토큰 **전부 스킵**, `arena_template_key` 프리팹을 `roomGO` 자식(로컬원점=방중앙, 회전 `90°×quarterTurns`)으로 인스턴스화. NavMesh 베이크 전에 들어가 바닥 포함됨.
- 공통 후속: `AttachRoomClearController`가 `GetComponentInChildren<MonsterSpawner/BossSpawner>`로 **스포너 자동 수집** → `RoomWaveController`. 부트스트래퍼는 스포너를 보관하지 않는다.

---

## 6. 보스 방 / 커스텀 아레나 제작

> 전체 설계·검증 항목: [boss-custom-arena-design.md](boss-custom-arena-design.md)

보스 방은 **손제작 프리팹**으로 만든다(길1+갈래1): "입구→통로/배경→트리거→아레나" 한 덩어리. 방 경계를 넘는 가림 전환 없이 한 화면에서 걸어가 보스 등장.

### 제작 체크리스트
1. **프리팹**(로컬원점=방중앙): 지형/콜라이더/장식/조명. 바닥 레이어=`Ground`, NavMesh 베이크 대상.
2. **보스 등장 위치**에 빈 GO + [BossSpawner](../Assets/RelicFairy/Characters/Monster/Monster/Core/BossSpawner.cs): `waitForExternalTrigger=true`. 보스는 `placedBoss`(프리팹 직배치) 또는 챕터 보스테이블 자동해석(런 중).
3. **트리거 GO** + `BoxCollider(isTrigger)` + [BossRoomController](../Assets/RelicFairy/Systems/Stage/World/BossRoomController.cs): `bossSpawner`/`barrier`(입구봉쇄, 비활성 시작)/`bossZoneCenter`(카메라 팬 타겟) 연결.
4. **Addressable 등록**: 주소 = `Arena_Boss_Ch1`(챕터별 Ch1~4), `StagePrefabs` 그룹.
5. **CSV 연결**: 보스 행 `arena_template_key = Arena_Boss_Ch1`.

흐름: 입구 스폰 → 통로 보행 → 트리거 도달 → 배리어 닫힘 + `Trigger()` → (IBossEntrance면) 카메라 팬 → 보스 Appear → 전투 → 처치 → 런 종료.

### 동작 모델 요약
- "보스라서 코드에서 예외"가 아니라 **`arena_template_key` 있는 행 = 커스텀맵**이라는 일반 규칙(다른 방도 키만 넣으면 적용 가능).
- grid_csv는 커스텀 아레나에서 **입구 문 + 플레이어 스폰만** 담당(지형·B·장식은 프리팹이 소유, 토큰 스킵).

### 현재 테스트 자산
- [Arena_Boss_Ch1.prefab](../Assets/RelicFairy/Systems/Stage/MapGen/Prefabs/Arena_Boss_Ch1.prefab) — ForestGuardian placedBoss + 트리거·배리어·플레이어스폰. BaseCamp에 비활성 파킹.
- ⚠️ **임시 테스트 설정**: `RunStructure_Default._bossThreshold = 0`(첫 방 직후 보스). 출시 전 **8로 복원**.

---

## 7. 등록 위치 치트시트

| 만든 것 | 등록 위치 | 키/필드 |
|---|---|---|
| 새 일반/특수 방 | `CHAPTER_N_ROOM_POOL.csv` 행 추가 | `pool_key`, `category`, `grid_csv` |
| 새 테마 외형 | BlockPalette SO + 부트스트래퍼 `Block Palettes` | `themeMatch` |
| 새 장식 | DecorationCatalog SO + 부트스트래퍼 `Decoration Catalogs` | `d<code>` |
| 새 토큰 동작 | `[TokenHandler]` 핸들러 클래스 | 토큰 문자 |
| 보스/특수 아레나 프리팹 | `StagePrefabs` Addressable + CSV `arena_template_key` | 주소 문자열 |
| 보스 진입 시점/마일스톤 | `RunStructure_Default.asset` | `_bossThreshold`, `_milestones` |

---

## 8. 자주 하는 실수 (격자 방)
- `P`(플레이어 스폰) 정확히 1개? 외곽 전부 `W`/`DR`로 폐쇄(낙사 방지)?
- 모든 행 길이 동일(직사각)? 공란 셀 없음(공란=무음 Floor)?
- Boss 방에 `B`(커스텀 아레나면 불필요) / Shop에 `Sw`·`Si` / Normal·Elite에 `M`/`m`?
- 입구(South `DR`) + 직진(North `DR`)? (Boss는 입구만)
- 장식 셀과 기능 셀 겹침 없음? `max_active_spawners` 의도와 일치?
