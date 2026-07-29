# 리치 보스 설계 문서 (Lich Boss Design Document)

> **작성일:** 2026-05-25  
> **최종 업데이트:** 2026-05-25 (패턴 확장 + 이동 시스템 추가)  
> **상태:** 기획 확정 (구현 전)  
> **참고 문서:** `BossDesign_Principles.md`, `RelicFairy_GameDesignDocument.md`

---

## 1. 컨셉 & 로어

**"멀린의 육체를 탈취한 외부 존재. 대마법사의 지식과 사신의 본질이 공존하는 2중 정체성."**

| 속성 | 내용 |
|------|------|
| **포지션** | Chapter 4 최종 보스 |
| **정체** | 멀린의 육체를 얻은 대마법사 몬스터 |
| **전투 스타일** | 다양한 원소 마법(화염·빙결·번개) + 사신 기술(낫·해골 소환·안개) |
| **이동 방식** | 공중 부유. 지상 보스 규칙 미적용. `BossDesign_Principles.md §12` 준수 |
| **기본 자세** | 미세 호버링 ±0.15m, 주기 2s. 로브·소매 PhysicBone 반응 |

### 2중 정체성 표현

```
Phase 1 (HP 100~40%)   → 대마법사 우세: 원소 마법 + 소환 + 공간 장악
Phase 2 Entry          → 사신의 본질 해방: 낫 소환, 정체성 전환 시네마틱
Phase 2 (HP 40~0%)     → 사신 + 대마법사 융합: 낫 + 강화 마법 + 안개 + 해골 군단
```

---

## 2. 이동 시스템 (Movement System)

엘든링 보스 분석에서 도출. **이동 자체가 공격의 예고이자 페인트**가 되어야 한다.

### 2-1. 이동 상태 정의

| 상태 ID | 이름 | 설명 | 시각 신호 |
|---------|------|------|----------|
| `IDLE_HOVER` | 기본 부유 | 미세 상하 진동 ±0.15m, 2s 주기. 다음 패턴 계산 중 | 완전 정지에 가까운 부유 |
| `ADVANCE_FLOAT` | 접근 부유 | 플레이어 방향으로 중속 접근 (2~3m/s) | 로브가 앞쪽으로 흘러내림, 몸 앞으로 기움 |
| `RETREAT_FLOAT` | 후퇴 부유 | 플레이어 반대 방향으로 물러남 (2m/s) | 로브가 뒤쪽으로 쓸림 |
| `CIRCLE_STRAFE` | 선회 | 플레이어 기준 시계·반시계 방향 궤도 이동 | 몸이 옆으로 기움, 시선은 플레이어 고정 |
| `DASH_CLOSE` | 대시 접근 | 0.4s에 4m 급부유. 근거리 패턴 전 | 몸 앞으로 급격히 기움, 로브 급격히 뒤로 |
| `ALTITUDE_RISE` | 고도 상승 | 2~4m 상승. AOE·소환 패턴 전 | 망토 아래로 처짐, 팔 벌림 |
| `ALTITUDE_DESCEND` | 고도 하강 | 플레이어 방향으로 하강 | 낫(P2)이 앞으로 내려옴 |
| `TELEPORT` | 순간이동 | 즉각 위치 이동. 출발지 잔상 VFX + 도착지 충격파 | 보라색 잔상 0.5s |

### 2-2. 거리 존 (Distance Zones)

```
[즉시 공격 존]   [최적 공격 존]   [추격 존]    [원거리 존]
    0 ~ 3m          3 ~ 6m         6 ~ 12m       12m+
       ↓               ↓               ↓            ↓
  근접 콤보 or     주력 패턴 실행   ADVANCE or    DASH_CLOSE or
  RETREAT_FLOAT    CIRCLE_STRAFE   DASH_CLOSE    TELEPORT 강제
```

### 2-3. 거리 존별 이동 결정

| 존 | 거리 | 이동 상태 | 선호 패턴 |
|----|------|---------|----------|
| **밀착** | 0~3m | `RETREAT_FLOAT` or 즉시 공격 | ScytheSweep, BlinkStrike |
| **최적** | 3~6m | `IDLE_HOVER` or `CIRCLE_STRAFE` | MagicBolt, ElementalCross, TeleportStrike |
| **추격** | 6~12m | `ADVANCE_FLOAT` or `DASH_CLOSE` | TeleportStrike, ScytheTornado |
| **원거리** | 12m+ | `TELEPORT` 강제 | SkeletonSummon (소환하며 거리 유지) |

### 2-4. 이동이 예고가 되는 언어 설계

```
이동 상태                   → 의미 (플레이어가 읽어야 할 것)
─────────────────────────────────────────────────────────────
RETREAT_FLOAT 후 정지       → DeathRay or 원소 집중 마법 예고
CIRCLE_STRAFE 후 갑자기 정지→ TeleportStrike or BlinkStrike 예고
ALTITUDE_RISE + 팔 벌림     → AOE 마법 or 해골 소환 예고
DASH_CLOSE + 몸 기움        → ScytheSweep or 근접 콤보 예고
IDLE_HOVER 1.5s 이상 정지   → 페인트. 아무것도 안 함 → 플레이어 롤 유도
TELEPORT                    → 항상 다른 위치에서 패턴 즉시 시작
```

### 2-5. Phase별 이동 특성 차이

| 항목 | Phase 1 | Phase 2 |
|------|---------|---------|
| 기본 속도 | 2m/s | 2.8m/s |
| IDLE_HOVER 지속 | 1.5~2.5s | 0.8~1.5s |
| CIRCLE_STRAFE 빈도 | 낮음 | 높음 (더 자주 선회) |
| TELEPORT 빈도 | 낮음 (TeleportStrike 패턴에만) | 높음 (이동 수단으로도 사용) |
| RETREAT_FLOAT 조건 | 피격 후에만 | 원거리 패턴 전에도 의도적 후퇴 |
| 고도 변화 | 거의 없음 | ALTITUDE_RISE/DESCEND 빈번 |

---

## 3. 패턴 목록

### Phase 1 패턴 (10종)

| ID | 패턴명 | Weight | Threat | 선행 이동 | 개요 |
|----|--------|--------|--------|---------|------|
| P1-1 | **MagicBolt** (마법볼트) | 25 | 2 | `IDLE_HOVER` | 원소 마법 구슬 발사. 3종 원소 중 무작위 |
| P1-2 | **TeleportStrike** (순간이동타격) | 15 | 3 | `TELEPORT` | 플레이어 뒤로 텔레포트 후 마법 근접 타격 |
| P1-3 | **ElementalCross** (원소 십자) | 15 | 2 | `ALTITUDE_RISE` 후 손으로 지목 | 리치 중심 ＋방향 4개 원소 장판. 대각선 안전 |
| P1-4 | **SkeletonSummon** (해골 소환) | 15 | 2 | `ALTITUDE_RISE` | 해골 2~3마리 소환. 소환 중 **무력화 포인트** |
| P1-5 | **ElementalBarrage** (원소 난사) | 15 | 2 | `IDLE_HOVER` 중 팔 들어올림 | 작은 마법탄 5발 빠르게 연속 발사. 플레이어 추적 |
| P1-6 | **ArcaneOrb** (추적 구체) | 10 | 3 | `RETREAT_FLOAT` 중 손에 구체 생성 | 느리게 8초간 추적하는 폭발 구체 1발 |
| P1-7 | **TimedRune** (시한 룬) | 10 | 2 | `ALTITUDE_RISE` 후 손으로 바닥 지목 | 바닥에 룬 설치. 2.5s 후 폭발. 최대 3개 동시 |
| P1-8 | **BlinkStrike** (순간 근접) | 10 | 3 | `CIRCLE_STRAFE` 중 갑자기 사라짐 | 플레이어 측면으로 0.8s 내 텔레포트 후 마법 찌르기 |
| P1-9 | **ElementalWave** (원소 파동) | 10 | 2 | `ADVANCE_FLOAT` 중 한 손을 아래로 | 지면을 따라 전진하는 에너지 파동. 도약으로 회피 |
| P1-10 | **ManaVortex** (마나 소용돌이) | 5 | 1 | `ALTITUDE_RISE` + 팔 오므림 | 리치 주변 끌어당기는 마력장 2s. 끌린 플레이어에게 후속 패턴 즉시 연결 |

#### MagicBolt 원소 변형 (발동 시 1가지 무작위)

| 원소 | 색상 코드 | 부가 효과 |
|------|-----------|----------|
| **화염** | 주홍 | 착탄 지점에 3초 화염 장판 생성 |
| **빙결** | 청록 | 피격 시 0.8s 이동속도 40% 감소 |
| **번개** | 노랑 | 착탄 후 반경 1.5m 연쇄 번개 1회 (해골에게도 유효) |

---

### Phase 2 Entry (전멸기)

| ID | 패턴명 | Threat | 개요 |
|----|--------|--------|------|
| E-1 | **SoulHarvest** (영혼 수확) | 5 | 거대 낫 소환 후 360도 회전 2바퀴. 전멸기. 가장자리 안전지대(흰색 테두리 빛) |

---

### Phase 2 패턴 (10종)

| ID | 패턴명 | Weight | Threat | 선행 이동 | 개요 |
|----|--------|--------|--------|---------|------|
| P2-1 | **ScytheSweep** (낫 스윙) | 20 | 3 | `DASH_CLOSE` + 몸 기움 | 전방 180도 횡스윙. 고데미지 + 넉백 3m |
| P2-2 | **ScytheTornado** (낫 회오리) | 15 | 4 | `ALTITUDE_RISE` + 낫 들어올림 | 낫을 머리 위로 들고 필드를 가로지르는 회오리. 좌·우 2회 왕복 |
| P2-3 | **DeathRay** (데스레이) | 10 | 4 | `RETREAT_FLOAT` 후 정지 | 강력한 집중 레이저 빔 |
| P2-4 | **FogOfDeath** (죽음의 안개) | 15 | 2→4 | `ALTITUDE_RISE` 천천히 | 필드 전체 안개 + 분신 기믹 (§4 상세) |
| P2-5 | **SkeletonLegion** (해골 군단) | 15 | 3 | `ALTITUDE_RISE` | 낫 해골 2 + 마법 해골 2 동시 소환. **무력화 포인트** |
| P2-6 | **ScytheThrow** (낫 투척) | 10 | 3 | `ALTITUDE_RISE` + 낫 들어올림 | 낫을 부메랑처럼 투척. 가로 방향 왕복. 점프 회피 |
| P2-7 | **ShadowStep** (그림자 스텝) | 10 | 3 | `CIRCLE_STRAFE` 급가속 후 사라짐 | 3연속 텔레포트하며 각 위치에서 낫 찌르기. 그림자 잔상 |
| P2-8 | **DarkNova** (암흑 폭발) | 5 | 5 | `IDLE_HOVER` 중 검은 오라 + 팔 교차 | 충전 3s → 전방위 폭발. 충전 중 임계 피해 누적 시 캔슬 가능 |
| P2-9 | **GravityGrasp** (중력 포획) | 10 | 4 | `RETREAT_FLOAT` 최대 거리 확보 후 손 뻗음 | 플레이어를 원거리에서 끌어당겨 1.5s 공중 부유 → 낙하 폭발 |
| P2-10 | **PhantomArray** (허상 진열) | 5 | 3 | `TELEPORT` → 중앙 착지 | 분신 4개가 각기 다른 방향에서 낫 스윙. 진짜만 피격 판정 |

---

## 4. 핵심 기믹 설계

### 기믹 1: 원소 장판 상호작용 (Phase 1)

```
화염 장판 + 빙결 장판 겹침 → 폭발 (반경 3m 피해. 리치에게도 유효)
번개 볼트 → 해골 적중 시 해골 1.5s 스턴
화염 장판 위 플레이어      → 지속 화상 데미지
```

### 기믹 2: 해골 누적 위협

```
ActiveSkeletonCount ≥ 5 → 리치 공격 속도 +20%
ActiveSkeletonCount ≥ 8 → 전멸기 예고 연출 3s → 폭발 전멸기 발동
해골 처치 시 마나 구슬(초록) 드롭 → 획득: 3초간 마법 피해 20% 감소 버프
```

### 기믹 3: 죽음의 안개 — 분신 기믹 (Phase 2)

```
1. 리치가 3초에 걸쳐 필드 전체에 안개 확산
2. 시야 반경 30%로 제한 (비네팅 강화)
3. 분신 3개 생성 (진짜 1 + 가짜 2)

   진짜 리치 : 보라색 마법 오라 + 낫이 미세하게 발광
   가짜 리치 : 회백색 오라 + 이동 패턴 약간 어색

4. 가짜 공격 시 : 반사 피해(경미) + 해골 1마리 소환
5. 진짜 3회 공격 성공 → 안개 즉시 해제
6. 30초 이내 실패 → 경고 없이 ScytheTornado 즉시 발동
```

### 기믹 4: 무력화 포인트 (SkeletonSummon / SkeletonLegion)

```
소환 모션 1.2s 동안 리치 무력화 게이지 노출 (파란색 게이지)
무력화 성공 → 해골 소환 취소 + 리치 1.5s 경직 (딜 타임)
무력화 실패 → 해골 소환 완료 + 리치 즉시 다음 패턴 진행
```

---

## 5. 이동 + 패턴 연계 콤보

엘든링 설계 원칙: 이동과 패턴이 자연스럽게 이어져 **하나의 무브셋처럼** 느껴져야 한다.

### 콤보 A — "추격 폭격" (도망칠 때 처벌)

```
플레이어가 거리를 벌림
→ ADVANCE_FLOAT (1.5s 추격)
→ ElementalBarrage (5발 난사, 도망치는 방향으로 조준)
→ IDLE_HOVER (0.8s 짧은 숨)
→ ArcaneOrb 발사 (추적 구체 — 도망치면 계속 따라옴)
```

### 콤보 B — "페인트 배후" (롤 유도 후 처벌)

```
CIRCLE_STRAFE 2s (플레이어 주위를 돌며 긴장감 조성)
→ 갑자기 IDLE_HOVER 0.5s 정지 (롤 유도 페인트)
→ BlinkStrike (플레이어가 롤한 반대 방향으로 즉시 텔레포트 + 찌르기)
```

### 콤보 C — "끌어당겨 몰아치기" (Phase 2 간판 콤보)

```
RETREAT_FLOAT 2m 후퇴
→ ManaVortex 발동 (끌어당기기 2s)
→ 플레이어가 끌려온 직후 ScytheSweep (낫 횡스윙)
→ DASH_CLOSE (즉시 따라붙기)
→ ShadowStep (3연 텔레포트 낫 찌르기)
```

### 콤보 D — "고도 압박" (공간 완전 장악)

```
ALTITUDE_RISE 2m 상승 (위협감 조성 1.5s)
→ TimedRune 3개 동시 투하 (바닥 장악)
→ ALTITUDE_DESCEND 하강하며 접근 (압박)
→ ElementalCross 발동 (장판이 깔린 상태에서 십자 추가)
→ 플레이어가 설 자리 없어짐
```

---

## 6. 3-레이어 패턴 선택 시스템

로스트아크 레이드 분석에서 도출한 **"스크립트 뼈대 + 가중치 풀 + 우선순위 큐"** 하이브리드 구조.

```
┌──────────────────────────────────────────────────────────────┐
│  Layer 1: SCRIPTED SPINE (고정 뼈대) — HP 트리거 기믹        │
│  항상 동일하게 발생. 전투의 서사적 이정표 역할.              │
├──────────────────────────────────────────────────────────────┤
│  Layer 2: PRIORITY QUEUE (조건부 강제 패턴)                   │
│  특정 게임 상태일 때 가중치 풀보다 먼저 실행됨.             │
├──────────────────────────────────────────────────────────────┤
│  Layer 3: WEIGHTED RANDOM POOL (가중치 랜덤 풀)              │
│  뼈대·우선순위 외 구간을 채우는 패턴. Phase마다 풀 교체.    │
└──────────────────────────────────────────────────────────────┘
```

### Layer 1: 뼈대 타임라인 (HP 트리거)

| HP | 이벤트 | 내용 |
|----|--------|------|
| **100% 진입** | MagicBolt×1 고정 | 화염 버전. 느린 Opening. 학습 기회 |
| **HP 75%** | SkeletonSummon 고정 | 해골 기믹 첫 소개. 무력화 포인트 안내 |
| **HP 60%** | ElementalCross 고정 | 장판 기믹 첫 소개. 느린 버전 |
| **HP 40%** | Phase 2 Entry 고정 | SoulHarvest 전멸기. §7 타임라인 실행 |
| **HP 25%** | FogOfDeath 고정 | 안개 분신 기믹 첫 발동 |
| **HP 10%** | ScytheTornado + SkeletonLegion 동시 | 최후 총력전 연출 |

### Layer 2: 우선순위 큐 (조건부 강제)

| 조건 | 강제 발동 | 이유 |
|------|---------|------|
| 거리 > 12m | TELEPORT 이동 후 패턴 | 원거리 도주 차단 |
| 거리 > 8m, TeleportStrike 쿨다운 없음 | TeleportStrike 강제 | 중거리 도주 처벌 |
| ActiveSkeletonCount ≥ 5 | 다음 패턴 ScytheSweep 강제 | 해골 방치 압박 |
| IsFogActive == true | 모든 패턴 차단 | 안개 기믹 처리 대기 |
| LastPatternTag == 선택 패턴 | 풀에서 재추첨 | 동일 패턴 연속 금지 |

### Layer 3: 가중치 풀 (Phase별)

**Phase 1 풀:**

| 패턴 | Weight | 조건 |
|------|--------|------|
| MagicBolt | 25 | LastBoltElement와 다른 원소 선택 |
| TeleportStrike | 15 | — |
| ElementalCross | 15 | HP ≤ 60% 이후 활성 |
| SkeletonSummon | 15 | ActiveSkeletonCount < 4 |
| ElementalBarrage | 15 | — |
| ArcaneOrb | 10 | — |
| TimedRune | 10 | ActiveRunes < 3 |
| BlinkStrike | 10 | 거리 < 8m |
| ElementalWave | 10 | 거리 < 10m |
| ManaVortex | 5 | 거리 < 6m |

**Phase 2 풀 (Phase 1 풀 완전 교체):**

| 패턴 | Weight | 조건 |
|------|--------|------|
| ScytheSweep | 20 | 거리 < 6m |
| ScytheTornado | 15 | — |
| DeathRay | 10 | — |
| FogOfDeath | 15 | !IsFogActive, 쿨다운 25s |
| SkeletonLegion | 15 | ActiveSkeletonCount < 3 |
| ScytheThrow | 10 | 거리 > 4m |
| ShadowStep | 10 | — |
| DarkNova | 5 | 쿨다운 40s |
| GravityGrasp | 10 | 거리 > 6m |
| PhantomArray | 5 | 쿨다운 35s |

---

## 7. Phase 2 Entry 타임라인

`BossDesign_Principles.md §11` 템플릿 기반.

```
T+0.0s   HP 40% 도달 → 공격 중단·무적 진입
T+0.2s   카메라 줌인 (FOV -8도, 0.3s 보간)
T+0.5s   리치 두 팔을 벌리며 천천히 상승. 로브 아래로 흘러내림
T+0.8s   대사 팝업: "멀린의 힘만으로는… 부족해." (보라색 텍스트)
T+1.5s   전신 검은 안개 폭발 + 크로마틱 어베레이션 0.5s
T+1.8s   슬로모션 0.5×, 0.6s — 낫이 허공에서 구체화
T+2.4s   낫 장착 완료. 실루엣 변화 (망토 → 사신 로브)
T+2.6s   바닥 외곽부터 균열·붕괴 (전투 공간 70%로 축소)
T+3.0s   음악 전환
T+3.5s   SoulHarvest 발동 (가장자리 생존 가능)
```

---

## 8. 전투 리듬 & UX 설계

### 8-1. 패턴 5단계 타이밍

| 패턴 | ① Opening | ② Signal | ③ Attack | ④ End Pose | ⑤ Return | 총 길이 |
|------|-----------|----------|----------|------------|----------|---------|
| MagicBolt | 0.6s | 0.15s | 0.3s | 0.4s | 0.3s | **1.75s** |
| ElementalBarrage | 0.5s | 0.1s | 1.2s | 0.3s | 0.25s | **2.35s** |
| ArcaneOrb | 0.8s | 0.2s | 0.3s | 0.4s | 0.3s | **2.0s** |
| TimedRune | 0.7s | 0.15s | 0.3s | 0.35s | 0.3s | **1.8s** |
| TeleportStrike | 0.8s | 0.15s | 0.4s | 0.5s | 0.35s | **2.2s** |
| BlinkStrike | 0.3s | 0.1s | 0.35s | 0.4s | 0.3s | **1.45s** |
| ElementalWave | 0.7s | 0.15s | 0.8s | 0.4s | 0.3s | **2.35s** |
| ElementalCross | 1.0s | 0.2s | 0.6s | 0.5s | 0.3s | **2.6s** |
| ManaVortex | 0.8s | — | 2.0s | 0.4s | 0.3s | **3.5s** |
| SkeletonSummon | 1.2s | — | 2.0s | 0.5s | 0.3s | **4.0s** |
| ScytheSweep | 1.0s | 0.2s | 0.5s | 0.6s | 0.4s | **2.75s** |
| ScytheThrow | 0.9s | 0.2s | 1.5s | 0.5s | 0.35s | **3.45s** |
| ShadowStep | 0.4s | 0.1s | 1.8s | 0.5s | 0.3s | **3.1s** |
| ScytheTornado | 1.2s | 0.2s | 1.2s | 0.6s | 0.4s | **3.6s** |
| GravityGrasp | 1.0s | 0.2s | 2.5s | 0.6s | 0.4s | **4.7s** |
| DeathRay | 1.5s | 0.2s | 1.5s | 0.7s | 0.4s | **4.3s** |
| PhantomArray | 1.0s | 0.2s | 2.0s | 0.6s | 0.4s | **4.2s** |
| SkeletonLegion | 1.2s | — | 2.0s | 0.5s | 0.3s | **4.0s** |
| DarkNova | 1.5s | — | 3.0s | 1.0s | 0.5s | **6.0s** |
| SoulHarvest | 2.0s | 0.3s | 3.0s | 0.8s | 0.5s | **6.6s** |

**원칙:** End Pose는 Threat에 비례. 강한 공격일수록 반격 창이 길어야 공정함.

### 8-2. Idle State (패턴 사이 숨통)

패턴 ⑤ Return 완료 → 이동 상태 전환 → 다음 패턴 Opening 시작.  
이 이동 구간이 플레이어의 스킬 사용·회복·포지셔닝 기회.

| HP 구간 | Idle 이동 지속 | 느껴지는 리듬 |
|---------|-------------|--------------|
| 100~75% | 2.0 ~ 2.5s | 여유로움. 학습 기회 |
| 75~40%  | 1.5 ~ 2.0s | 조금 빨라지는 느낌 |
| 40~25%  | 1.2 ~ 1.5s | 압박감 증가 |
| 25~0%   | 0.8 ~ 1.2s | 숨막히는 속도감 |

```csharp
float hp01 = currentHp / maxHp;
float idleDuration = Mathf.Lerp(idleMin, idleMax, hp01);
```

### 8-3. 버스트 그루핑 구조

패턴 2~3개를 하나의 버스트로 묶고, 버스트 사이에 긴 이동 휴식 삽입.

```
[Burst A]                      [버스트 간 긴 이동]   [Burst B]           [뼈대 기믹]
볼트 → 블링크스트라이크         CIRCLE_STRAFE 2.5s   원소십자 → 볼트      SkeletonSummon
  ↑(짧은이동 1.5s)↑                                   ↑(1.5s)↑
```

| 위치 | 지속시간 | 이동 상태 | 목적 |
|------|---------|---------|------|
| 버스트 내 패턴 사이 | 1.2 ~ 1.5s | `IDLE_HOVER` or `CIRCLE_STRAFE` | 짧은 호흡. 다음 예고 읽기 |
| 버스트 사이 | 2.0~2.5s (P1) / 1.5~2.0s (P2) | `ADVANCE_FLOAT` or `CIRCLE_STRAFE` | 스킬·회복·포지셔닝 |
| 뼈대 기믹 직전 | 0.5s (긴박) or 2.0s (준비 유도) | 기믹에 따라 | 기믹 진입 연출 |

### 8-4. 숨쉬는 UX 5가지 장치

| 장치 | 내용 |
|------|------|
| **End Pose 시선 이탈** | 공격 후 리치가 잠깐 하늘을 보거나 팔을 내림 → "지금 안 공격해" 시각 신호 |
| **Idle 환경음 전환** | 공격 중 마법 충전음 → 이동 중 바람 소리·낮은 숨소리. 청각적 "쉬어도 된다" 신호 |
| **피격 Mercy Window** | 플레이어 넉백 시 다음 패턴 Opening 0.3s 자동 지연. 연타 방지 |
| **무력화 성공 딜 타임** | 카운터 성공 → 1.5s 경직 = 보상 구간. "잘했더니 쉬는 시간" 성취감 |
| **소환 = 리듬 전환** | 소환 중 리치 직접 공격 없음. "보스 vs 나" → "보스+잡몹 vs 나"로 전환해 리듬 환기 |

### 8-5. Phase별 리듬 요약

```
Phase 1 — 가르치는 리듬 (여유 있는 ⬜ 구간)
⬛볼트 ⬜이동 ⬛텔포 ⬜⬜이동 ⬛원소십자 ⬜이동 ⬛볼트 ⬜⬜⬜이동 ⬛소환(기믹)

Phase 2 — 몰아붙이는 리듬 (⬜ 구간 짧고 덜 잦음)
⬛낫스윙 ⬜ ⬛그림자스텝 ⬜ ⬛회오리 ⬜안개기믹(긴숨) ⬛낫스윙 ⬜ ⬛데스레이
단, FogOfDeath 전후로 긴 숨 1회 삽입 → 완전 질식 방지
```

---

## 9. 이동 UX 신호 일람표

플레이어가 이동 상태를 읽고 대응을 결정해야 한다.

```
이동 상태            시각 신호                       플레이어 대응
────────────────────────────────────────────────────────────────────────
ADVANCE_FLOAT      로브가 앞쪽으로 흘러내림           준비 자세. 패턴 대기
RETREAT_FLOAT      로브가 뒤쪽으로 쓸림              거리 추격 or 데스레이/원거리 대비
CIRCLE_STRAFE      몸이 옆으로 기움                  방향 유지하며 따라돌기. 텔레포트 주의
ALTITUDE_RISE      망토 아래로 처짐, 몸 상승          AOE/소환 대비, 안전지대 탐색
ALTITUDE_DESCEND   낫이 앞으로 내려옴                백스텝 or 롤 준비
IDLE_HOVER 1.5s+   완전 정지                         페인트 의심. 롤 금지
DASH_CLOSE         몸이 급격히 앞으로 기움             즉시 롤 or 방어
TELEPORT           보라색 잔상 VFX                   도착지 충격파 회피 준비
```

---

## 10. 전체 패턴 조건 요약

| 패턴 | Phase | 발동 조건 | 쿨다운 |
|------|-------|---------|--------|
| MagicBolt | 1 | 항상 | 4s |
| ElementalBarrage | 1 | 항상 | 6s |
| ArcaneOrb | 1 | 항상 | 12s |
| TimedRune | 1 | ActiveRunes < 3 | 8s |
| TeleportStrike | 1 | 거리 > 4m | 8s |
| BlinkStrike | 1 | 거리 < 8m | 10s |
| ElementalWave | 1 | 거리 < 10m | 10s |
| ElementalCross | 1 | HP ≤ 60% 이후 | 12s |
| ManaVortex | 1 | 거리 < 6m | 18s |
| SkeletonSummon | 1 | ActiveSkeletonCount < 4 | 15s |
| ScytheSweep | 2 | 거리 < 6m | 6s |
| ScytheThrow | 2 | 거리 > 4m | 10s |
| ShadowStep | 2 | 항상 | 12s |
| ScytheTornado | 2 | 항상 | 14s |
| GravityGrasp | 2 | 거리 > 6m | 16s |
| DeathRay | 2 | 항상 | 18s |
| PhantomArray | 2 | 쿨다운 | 35s |
| FogOfDeath | 2 | !IsFogActive | 25s |
| DarkNova | 2 | 쿨다운 | 40s |
| SkeletonLegion | 2 | ActiveSkeletonCount < 3 | 20s |

---

## 11. LichBlackboard 필드 명세

```csharp
// 현재 구현됨
public float MagicBoltCooldown;
public float TeleportStrikeCooldown;
public float DeathRayCooldown;
public bool IsPhase2 { get; private set; }

// 추가 필요 — Phase 1
public float ElementalCrossCooldown;
public float ElementalBarrageCooldown;
public float ArcaneOrbCooldown;
public float TimedRuneCooldown;
public float BlinkStrikeCooldown;
public float ElementalWaveCooldown;
public float ManaVortexCooldown;
public float SkeletonSummonCooldown;
public int ActiveSkeletonCount;
public int ActiveRuneCount;
public int LastBoltElement;         // 원소 중복 방지 (0=화염, 1=빙결, 2=번개)

// 추가 필요 — Phase 2
public float ScytheSweepCooldown;
public float ScytheThrowCooldown;
public float ShadowStepCooldown;
public float ScytheTornadoCooldown;
public float GravityGraspCooldown;
public float FogOfDeathCooldown;
public float DarkNovaCooldown;
public float PhantomArrayCooldown;
public float SkeletonLegionCooldown;
public bool IsFogActive;
public int FakeAttackCount;

// 이동 시스템
public MovementState CurrentMovementState;
public float DistanceToPlayer;
public float MovementStateTimer;
```

---

## 12. 미구현 / 추후 작업 목록

- [ ] LichAnimatorController 에셋 생성 ("Lich/LichAnimator" Addressable 등록)
- [ ] GameScene_LichTest NavMesh 베이킹
- [ ] 이동 상태 머신 구현 (MovementState enum + 전환 로직)
- [ ] Phase 2 Entry 시네마틱 구현 (§7 타임라인)
- [ ] 히트스톱 시스템 구현 (`BossDesign_Principles.md §3`)
- [ ] 해골 AI: 낫 해골(근거리) / 마법 해골(원거리 마법볼트) 분리 구현
- [ ] FogOfDeath 분신 생성 로직 + 진짜/가짜 판별 시스템
- [ ] 원소 장판 상호작용 (화염+빙결 폭발)
- [ ] TimedRune 바닥 설치 + 폭발 시스템
- [ ] ArcaneOrb 추적 발사체
- [ ] GravityGrasp 물리 기반 끌어당기기
- [ ] DarkNova 충전 캔슬 조건 (임계 피해 누적)
- [ ] 콤보 시퀀서: 이동→패턴 자동 연결 로직
- [ ] ApplyPhase2Buffs() 버그 수정: bossConfig 수정값이 2회차 전투에 누적되는 문제

---

*최종 업데이트: 2026-05-25*
