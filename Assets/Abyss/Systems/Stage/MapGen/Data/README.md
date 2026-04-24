# Map Block Data — 테마별 확장 가이드

방 외형은 `MapRoomEntry.theme`(JSON) 값과 `BlockPalette.themeMatch`를 매칭해 결정된다.
매칭 실패 시 `themeMatch="*"` 범용 팔레트로 폴백.

## 현재 매칭 상태

| 방 테마 | 팔레트 | 결과 |
|---|---|---|
| `Forest` | `BlockPalette_Forest` | Forest 전용 외형 + Forest 장식 |
| 그 외 / 빈값 | `BlockPalette_Default` (fallback) | 중립 기본판 외형 + 장식 없음 |

- `BlockPalette_Default.asset`의 Floor/Wall/Obstacle은 `BD_Floor/Wall/Obstacle` (비테마 기본판) 참조
- 공용 기능 블록(`BD_MonsterSpawn`, `BD_PlayerSpawn`, `BD_BossSpawn`, `BD_ShopStall`)은 모든 팔레트가 공유
- `DecorationCatalog_Default.asset`은 의도적으로 `entries: []` — 테마 미매칭 시 장식 없음

## 새 테마 추가 절차

예: `Cave` 테마 추가.

1. **테마 전용 BlockDef 3종 생성** (`Assets/Abyss/Systems/Stage/MapGen/Data/`)
   - `BD_Floor_Cave.asset` (tileType = 0, Cave Floor 프리팹)
   - `BD_Wall_Cave.asset` (tileType = 1, Cave Wall 프리팹)
   - `BD_Obstacle_Cave.asset` (tileType = 2, Cave Obstacle 프리팹)

2. **BlockPalette 생성** (`Assets/Abyss/Systems/Stage/MapGen/Data/BlockPalette_Cave.asset`)
   - `themeMatch = "Cave"`
   - `blocks`에 위 3개 + 공용 4개(Monster/Player/Boss/Shop) 등록

3. **DecorationCatalog 생성 (선택)** (`Assets/Abyss/Settings/DecorationCatalog_Cave.asset`)
   - `themeMatch = "Cave"`
   - `grid_csv`에서 쓸 `d<code>` 토큰 → 프리팹 매핑 등록

4. **Bootstrapper 등록** (`StageMap` 씬의 `GameRunBootstrapper` 컴포넌트)
   - `Block Palettes` 배열에 `BlockPalette_Cave` 드래그
   - `Decoration Catalogs` 배열에 `DecorationCatalog_Cave` 드래그 (카탈로그 만들었을 때만)

이후 `MapRoomEntry.theme = "Cave"`인 방이 해당 팔레트로 렌더됨.

## TileType ↔ 정수값

| 정수 | TileType | grid_csv |
|---|---|---|
| 0 | Floor | F |
| 1 | Wall | W |
| 2 | Obstacle | O |
| 3 | MonsterSpawn | M |
| 4 | MonsterSpawnCandidate | m |
| 5 | PlayerSpawn | P |
| 6 | BossSpawn | B |
| 7 | ShopStall | S |
| 8 | NPCSpawn | N |
| 9 | Entrance | E |
| 10 | Exit | X |
| 11 | Trap | T |
| 12 | Chest | C |
| 13 | Empty | . |
| 14 | BuffBox | R |
| 15 | BuffPedestal | D |

`BD_<Name>.asset`의 `tileType` 필드는 이 정수와 일치해야 한다.
enum 중간 삽입 시 기존 BD 에셋은 **자동 마이그레이션되지 않으므로**
정수값을 일괄 시프트해야 한다 (Unity는 enum을 int로 직렬화).
