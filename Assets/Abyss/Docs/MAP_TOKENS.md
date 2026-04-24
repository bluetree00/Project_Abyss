# 맵 토큰 명세 (기획자용)

`STAGEDATA_MAP` 시트 / JSON의 `grid_csv` 한 셀에 들어갈 수 있는 문자 토큰과, 각 토큰이 실제 게임에서 어떤 오브젝트/동작으로 이어지는지 정리한 문서.

---

## 1. 그리드 기본 규칙

- **크기**: 30×30 기본 (가변 가능, `width`/`height`는 `grid_csv` 크기로 자동 계산되어 사실상 무시됨)
- **행 구분**: `;` (세미콜론) 또는 개행
- **열 구분**: `,` (쉼표)
- **CSV 첫 줄 = 맵 맨 윗줄** (z축 반전되어 렌더링됨. 플레이어 시점에서 "위쪽"이 CSV 첫 줄)
- **비어있는 셀**: `F` (Floor) 로 채움. 공란 금지
- **테두리**: 보통 `W` (Wall) 로 둘러쌈

---

## 2. 기본 타일 토큰

단일 문자, 1셀 = 1오브젝트. 대소문자 구분됨.

| 토큰 | TileType | 역할 | 설명 |
|------|----------|------|------|
| `F` | Floor | 바닥 | 플레이어/몬스터 이동 가능 |
| `W` | Wall | 벽 | 이동 불가, 맵 경계 |
| `O` | Obstacle | 장애물 | 이동 불가, 맵 내부 장식 겸 가림막 |
| `P` | PlayerSpawn | 플레이어 시작점 | 방 진입 시 플레이어 위치. 방당 1개 |
| `B` | BossSpawn | 보스 스폰 | Boss 카테고리 방에서 보스 생성 위치 |
| `S` | ShopStall | 상점 진열대 | Shop 카테고리 방에서 상품 진열 위치 |
| `N` | NPCSpawn | NPC | Event 방 등에서 NPC 배치 |
| `E` | Entrance | 입구 마커 | (현재 미사용 예약) |
| `X` | Exit | 출구 마커 | (현재 미사용 예약) |
| `T` | Trap | 함정 | (현재 미사용 예약) |
| `C` | Chest | 보물상자 | Event 방 등에서 보상 상자 |
| `R` | BuffBox | 버프 상자 | 상호작용 필요 — 플레이어가 눌러야 버프 발동 |
| `D` | BuffPedestal | 버프 발판 | 즉시 발동 — 플레이어가 밟으면 버프 |
| `.` | Empty | 구멍 | 블록 없음 (빈 공간) |

---

## 3. 몬스터 스포너 토큰

형식: **`[M|m][c|r|e]<숫자>`**  (예: `Mc3`, `mr5`, `Me1`)

### 3-1. 첫 글자 — 활성화 방식

| 첫 글자 | 의미 |
|---|---|
| `M` (대문자) | **확정 스폰**: 항상 활성화됨. 방에 들어오면 반드시 이 자리에서 몬스터 출현 |
| `m` (소문자) | **후보 스폰**: 방의 `max_active_spawners` 한도 내에서 랜덤 선택. 선택되지 않으면 해당 셀은 `Floor`로 대체 |

### 3-2. 두 번째 글자 — 등급 상한

해당 스포너가 소환할 수 있는 **최대 몬스터 등급**.

| 문자 | 등급 | 설명 |
|---|---|---|
| `c` | Common | 일반 몬스터만 |
| `r` | Rare | Common + Rare 가능 |
| `e` | Elite | Common + Rare + Elite 모두 가능 |

### 3-3. 숫자 — 해당 스포너가 소환할 총 마릿수

| 값 | 의미 |
|---|---|
| `0` | 무제한 (테스트 혹은 지속 스폰용) |
| `1~N` | 정확히 N마리 소환 후 해당 스포너 종료 |

### 3-4. 예시

| 토큰 | 뜻 |
|---|---|
| `Mc3` | 확정 스폰, Common 등급까지, 3마리 |
| `mc3` | 후보 스폰, Common 등급까지, 3마리 (max로 활성 여부 결정) |
| `Mr5` | 확정 스폰, Rare 등급까지, 5마리 |
| `Me1` | 확정 스폰, Elite 등급까지, 1마리 (보통 엘리트 방에 사용) |
| `mr4` | 후보 스폰, Rare 등급까지, 4마리 |
| `M` | 별칭 = `Mc0` (Common, 무제한). 하위호환용 |

---

## 4. 장식 토큰 (Decoration)

형식: **`d<code>`** (`d` 다음에 1글자 이상의 코드)

- 파싱 시 해당 셀은 `Floor`로 기록되고, 장식은 **후처리로 스폰**됨 (바닥은 깔림)
- 테마별 `DecorationCatalog` SO에 등록된 코드만 실제 오브젝트로 스폰되며, 등록 안 된 코드는 무시됨

### 4-1. 현재 Forest 테마 등록 코드 (`DecorationCatalog_Forest.asset`)

| 토큰 | code | 오브젝트 | 크기(scale) | Y 랜덤회전 |
|---|---|---|---|---|
| `dt` | t | Big Tree (큰 나무) | 0.40 | ✓ |
| `dp` | p | Small Tree (작은 나무) | 0.35 | ✓ |
| `dn` | n | (숲 오브젝트, 미확정) | 0.45 | ✓ |
| `df` | f | Fog (안개 파티클) | 1.50 | ✓ |
| `dl` | l | Leaves (잎사귀) | 1.00 | ✓ |
| `dm` | m | Magic Lights (매직 라이트) | 1.20 | ✓ |

> 다른 테마(Cave/Abyss/Castle/Throne)는 현재 전용 카탈로그가 없어 모두 **장식 미표시** (Default로 폴백).
> 기획자가 새 테마용 장식이 필요하면 사용할 code 목록을 정의 → 개발팀이 카탈로그 SO에 등록.

### 4-2. 주의사항

- 장식은 **바닥 위에 겹쳐** 배치됨. 몬스터·플레이어 이동은 방해하지 않음 (NavMesh 제외 처리)
- 장식 셀에는 `P`, `M`, `B` 같은 기능 타일을 겸할 수 없음 (한 셀에 하나의 토큰만)
- 장식 셀은 시각 효과 전용. 레벨 디자인 의도가 있다면 주변 `O`(장애물)나 `W`(벽)로 차폐 구성 병행 필요

---

## 5. 방 메타데이터 필드

`grid_csv` 옆에 행 단위로 함께 들어가는 방 설정.

| 필드 | 타입 | 설명 | 예시 |
|---|---|---|---|
| `room_id` | 문자열 | 방 고유 ID. snake_case + 번호 | `battle_001`, `elite_002`, `boss_001` |
| `category` | enum | 방 종류 | `Battle` / `Elite` / `Boss` / `Event` / `Shop` / `Start` |
| `theme` | 문자열 | 블록 외형 테마. 챕터 테마로 오버라이드될 수 있음 | `Forest` / `Cave` / `Abyss` / `Castle` / `Throne` |
| `palette` | 문자열 | (참고용 ID, 현재는 `theme`로 매칭) | `ForestT1` |
| `width` / `height` | 정수 | 참고 필드 (grid_csv에서 자동 계산됨) | `30` |
| `grid_csv` | 문자열 | 위 토큰으로 구성된 그리드 | (본문 참조) |
| `layout_rule` | 문자열 | 규칙 기반 생성 모드용 (현재 미사용) | `""` |
| `entrance` | 문자열 | 입장 연출 | `Scatter` / `Fade` / `Instant` / `WallDrop` |
| `scatter_range` | 실수 | 블록이 흩뿌려지는 반경 (유닛) | `15.0` |
| `return_duration` | 실수 | 블록 복귀 애니메이션 시간 (초) | `3.0` |
| `max_active_spawners` | 정수 | **아래 5-1 규칙 참고** | `3` |
| `stat_version` | 정수 | 데이터 버전 | `3` |

### 5-1. `max_active_spawners` 규칙

한 방에서 **동시에 활성화될 수 있는 스포너의 총 개수** 상한.

- `0` (또는 음수): 제한 없음 (`M`/`m` 모든 셀 활성)
- 확정(`M`) 개수 ≥ max: 확정만 모두 활성, 후보(`m`)는 전부 `Floor`로 대체
- 확정 `<` max: 확정 전부 + 남은 슬롯만큼 후보 중에서 **랜덤 선택**

**예시**: 방에 `Mc3` 2개 + `mc3` 2개가 있고 `max_active_spawners=3`이면
→ 확정 2 전부 활성 + 후보 2개 중 **랜덤 1개만** 활성 (나머지 후보는 Floor)

이 규칙으로 같은 맵이라도 플레이마다 약간씩 다른 몬스터 배치를 경험하게 된다.

---

## 6. 챕터 ↔ 테마 연결

`ChapterDataSO.theme` 값이 방의 `theme`를 오버라이드 (챕터 테마 우선 → 방별 theme → Default 폴백)

| 챕터 | 이름 | 테마 | 현재 블록 팔레트 |
|---|---|---|---|
| Chapter1 | 잊혀진 숲 | `Forest` | BlockPalette_Forest ✓ |
| Chapter2 | 어둠이 드리운 숲 | `Cave` | (미구현 → Default 폴백) |
| Chapter3 | 종말의 협곡 | `Abyss` | (미구현 → Default 폴백) |
| Chapter4 | 저주받은 성채 | `Castle` | (미구현 → Default 폴백) |
| Chapter5 | 종말의 왕좌 | `Throne` | (미구현 → Default 폴백) |

> Forest 외 테마는 전용 블록/장식 에셋이 준비되면 자동 반영됨. 기획자는 이 테마 문자열만 확정해주면 됨.

---

## 7. 카테고리별 작성 가이드

| category | 필수 배치 토큰 | 일반적으로 쓰는 토큰 |
|---|---|---|
| Battle | `P` (1개), 최소 1개 이상 `M`/`m` | `O`, 장식 |
| Elite | `P` (1개), `Me1` 계열 (엘리트 몬스터) | `O`, 장식 |
| Boss | `P` (1개), `B` (1개 또는 `B,B` 2연속) | `O`, 장식 |
| Event | `P` (1개), `N` 또는 `C` | 장식 |
| Shop | `P` (1개), `S` (여러 개 가능) | 장식 |
| Start | `P` (1개) | 장식, `O` (분위기용) |

---

## 8. 전체 예시 — battle_001 방 (일부 발췌)

```
Chapter 1 Battle 방, Forest 테마, 30×30

행 3 (위에서 3번째): W,F,F,F,F,dt,F,...,F,dt,F,F,F,F,W
  → 왼쪽 끝 W(벽) + 내부 바닥 + 5번째 열에 dt(큰 나무)
행 13 (중앙): W,F,F,F,F,F,F,F,Mc3,F,F,...,F,Mc3,F,F,F,F,F,F,F,W
  → 좌우 대칭 위치에 Mc3(확정 Common 3마리) 2개
행 25 (플레이어 시작 근처): W,F,...,F,P,F,...,F,W
  → 중앙에 P(플레이어 시작점)
```

`max_active_spawners = 3`이면 확정 2개 + 후보 1개만 활성 → 총 3개 스포너 작동.

---

## 9. 자주 하는 실수 체크리스트

- [ ] `P` 토큰이 정확히 1개인가? (0개면 플레이어가 스폰 안 됨)
- [ ] 맵 외곽이 `W`로 전부 둘러싸여 있는가? (뚫려 있으면 플레이어가 맵 밖으로 추락)
- [ ] Boss 방에 `B`가 있는가? / Shop 방에 `S`가 있는가?
- [ ] 장식 셀과 기능 셀(`M`, `P` 등)이 겹쳐있지는 않은가?
- [ ] `max_active_spawners`가 실제 스포너 설계 의도와 맞는가?
- [ ] 대소문자 구분: `M` ≠ `m`, `Mc3` ≠ `MC3` (현재 등급 문자 `c/r/e`는 대소문자 모두 허용되지만 소문자 권장)

---

## 부록 A. 시스템 내부 참조 (개발용)

- 타일 enum: [TileType.cs](../Systems/Stage/MapGen/TileType.cs)
- 파싱 로직: [MapDataLoader.cs](../Systems/Stage/MapGen/MapDataLoader.cs)
- 블록 팔레트: [Assets/Abyss/Systems/Stage/MapGen/Data/](../Systems/Stage/MapGen/Data/)
- 장식 카탈로그: [Assets/Abyss/Settings/](../../Settings/)
- 챕터 데이터: [Assets/Abyss/Systems/Stage/Stage/Data/Chapters/](../Systems/Stage/Stage/Data/Chapters/)

문서 관리: 토큰이 추가/변경되면 본 문서도 함께 갱신.
