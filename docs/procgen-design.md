# 절차적 생성 설계 (하데스형) — 최종본

> 2026-06-01 설계 확정. 구현 착수.

## 1. 목표 & 핵심 전환
- 고정 슬롯맵(`chapter_map`) **폐기** → **방 풀 + 런타임 시퀀서**
- **격리형 방 전환** (정통 하데스): 전체 경로를 연속 월드로 잇지 않음

## 2. 핵심 원칙 — "다음 방만 구현한다"
항상 **현재 방(+준비 중인 다음 방)만 상주**, 나머지 디스폰.
- AABB 겹침 / 헤딩 누적 / 굽이침 충돌 전부 불필요
- "턴·방향"은 미니맵·진입 측면 연출용 개념 레이어, 월드의 실제 굽이침 아님

## 3. 런타임 흐름
```
방 진입
  → RunSequencer가 출구 2개(직진/턴) 즉시 롤 (방 종류 확정)
  → 전투 중 백그라운드로 다음 방 에셋 프리로드
방 클리어 (RoomClearController)
  → 출구 문 개방 + 각 문에 '방 종류 아이콘' (물리 게이트)
문 통과
  → 다음 방 빌드(프리로드 에셋) + DissolveEntrance
  → 진입 측면을 선택 문에 맞춤, 이전 방 디스폰
  → visitCount++ …
보스 카운트 도달 → 고정 피날레 (보스 전방 → 보스)
```

## 4. 방 모델 — 1 입구 / 2 출구
| 방 | 입구 | 출구 |
|---|---|---|
| 시작 | 0 | N |
| 일반/정예 | 1 | 2 (직진 + 턴) |
| 상점/이벤트 | 1 | 1~2 |
| 보스 전방 | 1 | 1 (→보스 확정) |
| 보스 | 1 | 0 (종단) |

- 캐논 로컬: 입구=아래 / 직진=위 / 턴=옆(좌or우, 미러 결정)
- 문 선택 = 방 종류 + 다음 방 진입 위치 (왼문→왼쪽 등장)
- 인카운터는 `mc3r2`류 토큰 + `max_active_spawners` + `ApplyMonsterSpawnerPlan`로 방별 변동 → 별도 인카운터 시스템 불필요

## 5. 문 토큰 스킴
- **`DR<width>`** (생략 시 3) — 입구/직진/턴 엣지에 배치
- **구조 메타**로 처리: `MapDataLoader.Parse`가 `doorInfos` 딕셔너리로 추출 (`spawnInfos`/`decorationInfos` 패턴 확장)
- **기본 폐쇄**: 미선택 문 Wall 유지, 선택 문만 Floor 개방 (빌드 전 적용)

## 6. 미러 & 회전
`TokenParser`가 grid_csv 문자열을 재파싱하고 `MapBuilder`는 월드 기준 인스턴스화 → **트랜스폼 회전 금지**.
- 회전·미러는 **grid_csv 문자열 변환(열 반전/90° 회전) 후 재직렬화**, 변환본을 양쪽에 동일 공급
- 미러 = 변형 + 턴 좌/우 결정

## 7. 시퀀서 — 규칙 기반
```
방 클리어(visitCount):
  ① 보스 어프로치 미진입 & visitCount >= bossThreshold
       → 다음 = 고정 보스 전방 (단일, 분기 없음)
  ② 직전이 보스 전방
       → 다음 = 보스방 (확정)
  ③ 일반 페이즈
       → 2슬롯(직진/턴) 카테고리 롤:
           상점/이벤트 = 캡 미달 & 확률 / 정예 = 규칙 / 그 외 Normal
           (한 문쌍 특수방 최대 1개)
       → 카테고리별 pool_key 선택 (difficulty 윈도 + 쿨다운)
```
- `difficulty_scale` = visitCount 난이도 윈도 (Forest→Abyss 전환)

## 8. `RunStructureConfig` (SO)
| 필드 | 의미 |
|---|---|
| `bossThreshold` | 보스 어프로치 시작 방문 수 |
| `preBossRoom` (pool_key ×1) | 단일 고정 보스 전방 |
| `bossRoom` (pool_key) | 보스방 |
| `shopMaxPerChapter`, `shopChance` | 상점 캡 + 확률 |
| `eventMaxPerChapter`, `eventChance` | 이벤트 캡 + 확률 |
| `eliteChance` | 정예 등장 확률 |
| `difficultyByVisit` | visitCount→난이도/테마 곡선 |

## 9. 전환 & 준비
- 격리 + 리프프로그 앵커: 다음 방을 별도 고정 앵커에 빌드 → 이동 → 이전 방 디스폰
- 준비(기본): 진입 시 출구 롤 → 전투 중 에셋 프리로드 → 통과 시 지오메트리 빌드. 히치 시 숨김 프리빌드로 승급
- 재사용: AddressableManager, DissolveEntrance, RoomWave/Entry/Barrier/Clear 컨트롤러

## 10. 문 UI & 보상
- 문 아이콘 = 방 종류 (CategoryKor/Color 재사용)
- 보상은 현행 유지 (방 안 ClearRewardTrigger), 문엔 표시 안 함

## 11. 상태 / 세이브
- 런타임 방 식별 = visitCount(depth)
- 이어하기: 경로(pool_key 리스트) + visitCount + RNG 시드 + 특수방 사용 카운터 직렬화

## 12. 재사용 vs 폐기
| 재사용 | 폐기 |
|---|---|
| MapBuilder, TokenParser, ParsePoolCsv, ApplyMonsterSpawnerPlan | chapter_map 슬롯 CSV, ZoneMapSlot, MergeSlotPool |
| RoomClear/Wave/Barrier, DissolveEntrance, 물리 게이트 | PunchDoors, OpenWallsForConnections, CalcDoorMasks |
| AddressableManager 비동기 | E/X 방향 타일, world_center/lane/layer/next_zone_indices, AABB·헤딩 |

## 13. 신규 컴포넌트 (책임)
- **RunSequencer** — visitCount·구조 골격·특수방 카운터 보유, `RollExits()` (규칙 알고리즘), 보스 피날레 게이팅
- **연결 파이프라인** — 문 선택 → grid 변환(미러/회전) → 문 개방 → 빌드 → 진입 측면/게이트 → 이전 방 디스폰
- **GridTransform 유틸** — grid_csv 문자열 회전/미러
- **MapDataLoader.doorInfos** — DR 토큰 추출 (기존 패턴 확장)
- **RunStructureConfig SO** — #8 스키마

## 14. 남은 세부 (구현 시 확정)
- `DR` 폭/다중 문 표기, 진입 자동정렬 시 스포너·장식 좌표 검증
- 전환 카메라/연출, 미니맵(개념적 트리)
- `eliteRule` 구체화 (확률 vs 스케줄)

## 구현 단계
- **Phase 1 (기반)**: GridTransform, DoorInfo + MapDataLoader.doorInfos, RunStructureConfig
- **Phase 2 (시퀀서)**: DoorPlan/RoomPlanKind, RunSequencer
- **Phase 3 (통합)**: GameRunBootstrapper 연결 파이프라인, 물리 게이트 + 종류 아이콘, 세이브
