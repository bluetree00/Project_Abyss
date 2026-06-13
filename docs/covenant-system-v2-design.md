# 서약 시스템 v2 — 12종 재구축 설계서 (계획서)

> 대상: Unity 6, RelicFairy / 작성일: 2026-06-12
> 근거 기획서: "서약 기획서.pdf" (06 서약 시스템 — 12종 / 4카테고리 / 3단계)
> 상태: **계획 단계 — 코드 구현 전. 본 문서 합의 후 단계별 착수.**

---

## 0. TL;DR

- **시스템 골격(랜덤 3지선다 + 3단계 성장 + 데이터 구동 + 이벤트 디스패치)은 이미 존재하며 재사용한다.**
- 바꿔야 하는 건 **콘텐츠(로스터·효과)**와, 기획서가 요구하는 **신규 확장 지점 6종**이다.
- 현재 로스터 12종 중 이름만 4종 일치(galahad/arthur/morgana/nimue), 효과는 전부 불일치 → **8종 제거·4종 재작성·8종 신규**.
- 최대 난관은 **"공격 형태 변환" 파이프라인**(투사체화/관통/부채꼴/다중타격/판정횟수). 5개 서약이 의존 → 별도 추상화로 설계.
- 구현은 **의존도 낮은 카테고리부터** 4페이즈로 분리. 시스템 브리지(룬/보급/재선택)는 마지막.

---

## 1. 기획서 요약 (목표 스펙)

12종, 4카테고리, 각 3단계 = **선택(CH1)/강화(CH2)/각성(CH3)**. 단계는 기존 `CovenantStage.Basic/Enhanced/Evolved`와 1:1 대응.

| # | 서약(id 제안) | 카테고리 | 한 줄 테마 |
|---|---|---|---|
| 1 | 모르가나 `morgana` | 행동 조건형 | 무기 교체 시 이전 무기 잔상이 동시 공격 |
| 2 | 트리스탄 `tristan` | 행동 조건형 | 이동 중 공격이 투사체로 변환 |
| 3 | 라이오넬 `lionel` | 행동 조건형 | 같은 적 연속 적중 → 광역 충격파 |
| 4 | 레오데그란스 `leodegrance` | 런 구조형 | 주기마다 보급품(골드/아이템) 낙하 |
| 5 | 니무에 `nimue` | 런 구조형 | 그리드 블록 추가 + 속성 임계값 인하 |
| 6 | 기네비어 `guinevere` | 런 구조형 | 선택을 되돌리는 재선택(reroll) |
| 7 | 엘레인 `elaine` | 전투 리듬형 | 스킬 직후 일반공격이 쿨타임 단축 |
| 8 | 이졸데 `isolde` | 전투 리듬형 | 스킬↔일반 교차 누적 → 스킬 광역화 |
| 9 | 케이 경 `kay` | 전투 리듬형 | 주기마다 다음 공격 관통/부채꼴 |
| 10 | 베디비어 `bedivere` | 트레이드오프형 | 스킬쿨 포기 → 일반공격 다중 타격 |
| 11 | 갤러해드 `galahad` | 트레이드오프형 | 치명타 포기 → 최소피해 보장 |
| 12 | 아서왕 `arthur` | 트레이드오프형 | 범위 포기 → 단일 판정횟수 증가 |

세부 단계 수치는 기획서 페이지를 그대로 데이터(`CovenantDataSO` 배열)로 옮긴다(§6).

---

## 2. 기존 골격 (그대로 재사용)

| 자산 | 역할 | 재사용 |
|---|---|---|
| `CovenantPickup` / `WorldCovenantPickup.PickRandomOptionsExcluding` | 랜덤 3지선다 후보 구성 | ✅ 그대로 |
| `CovenantStage` (Basic/Enhanced/Evolved) | 3단계 성장 | ✅ = 선택/강화/각성 |
| `CovenantHandler` | 보유 목록·이벤트 디스패치·강화/진화·세이브 복원 | ✅ 골격 유지, 디스패치 보강 |
| `CovenantBase` (4 인터페이스 + V/VI + 헬퍼) | 서약 구현 기반 | ✅ 확장 |
| `CovenantContext` | Player/Stats/RunState/Session/DataTable 주입 | ✅ 필드 추가 가능 |
| `CovenantDataSO` / `CovenantDataTableSO` | 단계별 수치(float[]) 구동 | ✅ 카테고리 필드만 추가 |
| `CovenantFactory` | id→인스턴스 등록 테이블 | ♻️ 로스터 교체 |
| 세이브 (`CovenantSaveEntry` id+stage) | 이어하기 복원 | ✅ 그대로 |

**결론:** 프레임워크 교체가 아니라 **콘텐츠 교체 + 확장 지점 추가**다.

---

## 3. 갭 분석 (현재 → 필요)

### 3-1. 죽은 훅 (인터페이스 선언만 있고 디스패치 없음)
구현은 돼 있으나 **아무도 호출하지 않는** 메서드 — v2가 강하게 의존하므로 배선 필요:

| 훅 | 현재 | 필요 서약 |
|---|---|---|
| `OnAttackHit(target, dmg)` | 미발화 | lionel, arthur, elaine, isolde, bedivere, morgana |
| `OnSkillUse(skill)` | 미발화(패시브/룬만 받음) | elaine, isolde |
| `ModifyOutgoingDamage` | 미발화 | morgana(잔상 가산), galahad(최소피해), arthur |
| `ModifySkillEffect` / `OverrideSkillCost` | 미발화 | isolde, bedivere, elaine |

→ **선결 작업 P0: 디스패치 배선.** 전투 코드 소유자와 호출 지점(공격 적중·스킬 사용·피해 산출) 합의 필요.

### 3-2. 완전 신규 확장 지점 (현재 부재)

| 신규 지점 | 설명 | 필요 서약 |
|---|---|---|
| **공격 형태 변환 파이프라인** | 일반공격을 투사체/관통/부채꼴/다중타격/판정횟수로 변형 | tristan, kay, bedivere, arthur, (morgana/lionel 일부) |
| 무기 교체 이벤트 | `WeaponManager.OnWeaponChanged` 구독 → 서약 훅 | morgana |
| 이동 컨텍스트 | "이동 중 공격" 판정(속도/입력 플래그) | tristan |
| 치명타/최소피해 훅 | DamageFormula에 crit 고정·하한 보장 | galahad |
| 룬 브리지 | MerlinRune 블록 부여·임계값 인하 | nimue |
| 보급 드롭 API | 주기적 골드/아이템 지급 + 월드 픽업 | leodegrance |
| 재선택 브리지 | 아이템/그리드/스킬 선택 UI reroll | guinevere |

---

## 4. 확장 구조 설계 (핵심)

설계 원칙: **(a) 기존 4인터페이스를 깨지 않고 증분 확장, (b) "무엇을/언제"는 서약이, "어떻게"는 공통 헬퍼/파이프라인이, (c) 수치는 전부 데이터(`V/VI`) 구동, (d) 신규 시스템 의존은 브리지로 격리.**

### 4-1. 카테고리 메타데이터
- `enum CovenantCategory { ActionConditional, RunStructure, CombatRhythm, Tradeoff }`
- `CovenantDataSO`에 `category` 필드 추가. `CovenantBase`에 `virtual CovenantCategory Category` 노출.
- 용도: 선택 UI 색/태그, **카테고리별 후보 가중치**(예: 한 런에서 동일 카테고리 편중 방지) — `PickRandomOptionsExcluding` 확장 지점.

### 4-2. 이벤트 훅 보강
- **P0 배선**: `OnAttackHit`/`OnSkillUse`/`ModifyOutgoingDamage`를 전투 코드의 실제 적중·스킬·피해 산출 지점에서 `CovenantHandler` 경유로 호출.
- **신규 이벤트** (인터페이스 `ICovenantEventListener`에 default no-op 추가):
  - `OnWeaponSwap(WeaponData prev, WeaponData next)` — `WeaponManager.OnWeaponChanged` 어댑터에서 발화(이전 무기 캐싱 포함).
  - 이동 상태: 신규 이벤트 대신 **`CombatContext`에 `IsMoving`/`MoveDir` 필드 추가**(공격 산출 시 컨트롤러 상태 스냅샷). tristan이 읽음.

### 4-3. 공격 형태 변환 파이프라인 (A 확정 — 최대 난관)

> **결정(2026-06-12): A 확정.** 변환형 서약(tristan/kay/arthur/bedivere)은 사후 부가효과(B)로 흉내낼 수 없다. B는 §9에 따라 **가산형 한정 임시 수단**으로만 남긴다.

**현행 실측(중요):** 일반공격에 **통합 발사 초크포인트가 없다.** 근접과 투사체가 분리:
- 근접: `ActAttackState` 런지 캐스트가 직접 `IDamageable.TakeDamage` (ActAttackState.cs:~318).
- 투사체(활): 별도 투사체 프리팹 스폰. (`SwordAttackPolicy/BowAttackPolicy`는 입력 차지 정책일 뿐, 피해 발사 아님.)

따라서 A는 "초크포인트 추가"가 아니라 **공통 발사 시맨틱(seam) 신설 + 형태 실행기(shape executor) 신규 기능**을 의미한다.

일반공격 1회를 발사하기 직전, 근접·투사체 **양 경로가 공통으로 구성**하는 변형 가능한 **`AttackRequest`**:

```
struct AttackRequest {
    Vector3 origin, dir;
    float    baseMultiplier;
    int      hitCount;        // 판정 횟수 ×N (arthur, morgana echo) — 현재 1 고정
    float    radiusScale;     // 범위 배율 (arthur -50%)
    int      maxTargets;      // 동시 타격 대상 수 (bedivere) — 현재 단일 런지
    AttackShape shape;        // Default | Projectile | Pierce | Fan (tristan, kay)
    int      bounceCount;     // 적중 후 튕김 (tristan 강화)
    bool     isMovingAttack;  // tristan 조건 (CombatContext.IsMoving 스냅샷)
    WeaponData prevWeapon;    // morgana 잔상 참조
    // ... 확장 여지
}
enum AttackShape { Default, Projectile, Pierce, Fan }
```

- 흐름: **근접·투사체 경로 → `AttackRequest` 구성 → `CovenantHandler.ModifyAttack(ref req)` 순회 → 공통 shape executor가 형태별 해석.**
- 신규 인터페이스 `ICovenantAttackModifier.ModifyAttack(ref AttackRequest)`. 서약은 플래그만 세팅, **실행("어떻게")은 공통 executor**가 담당.
- **신규 전투 기능(=새 게임플레이 능력, 플러밍 아님):**
  - 근접→투사체 발사(tristan): 근접 무기에 투사체 발사 능력 자체 신설.
  - 관통/부채꼴(kay), 동시 N체(bedivere), 판정 ×N(arthur·morgana), 범위 배율(arthur), 튕김/연쇄(tristan).
- **최대 의존/리스크:** 이 seam은 전투 코드 소유권 영역. 근접(ActAttackState) + 투사체 두 경로를 공통 `AttackRequest`로 수렴시키는 작업이 P2의 임계경로다.

### 4-4. 손익(트레이드오프) 훅
- **치명타/최소피해**(galahad): `DamageFormula`(또는 그 호출부)에 서약 질의 훅 — `critChanceOverride`, `minDamageFloorRatio`. 단계별 확정 치명타 주기는 `OnAttackHit` 카운터로.
- **스킬 비용/쿨타임**(bedivere/elaine): `OverrideSkillCost` 배선 + 쿨타임 조정 API(스킬 시스템 소유자 합의).

### 4-5. 시스템 브리지 (의존 격리)
각 외부 시스템을 직접 만지지 않고 **얇은 브리지 인터페이스**로 결합:
- `IRuneCovenantBridge` (nimue): `GrantBlock(chapter)`, `LowerThreshold(stage)` — `MerlinRuneBridge` 구현.
- `ISupplyDropService` (leodegrance): `DropSupply(tier, gold|item)` — 인벤토리/드롭 시스템 구현.
- `IReselectionService` (guinevere): `GrantReroll(count, qualityBonus)` — 선택 UI(아이템/그리드/스킬) 구현.

브리지가 없으면 해당 서약은 **무동작(no-op)**으로 안전 폴백(참조 0 → 런 진행 무영향).

---

## 5. 12종 → 훅 매핑

| 서약 | 주 훅 | 신규 의존 | 난이도 |
|---|---|---|---|
| galahad | DamageFormula crit/floor 훅, OnAttackHit | 치명타/최소피해 훅 | ★★ |
| morgana | OnWeaponSwap, OnAttackHit/ModifyOutgoing, AttackContext.hitCount | 무기교체 이벤트 + 잔상 변환 | ★★★ |
| lionel | OnAttackHit(타깃별 연속 카운터) + DealAoe(기존) | OnAttackHit 배선만 | ★ |
| arthur | AttackContext(radiusScale·hitCount) + OnAttackHit(낙인) | 공격 변환 파이프라인 | ★★★ |
| elaine | OnSkillUse + OnAttackHit + 쿨타임 단축 API | 훅 배선 + 쿨타임 API | ★★ |
| isolde | OnSkillUse↔OnAttackHit 시퀀스 + ModifySkillEffect | 훅 배선 + 스킬효과 변환 | ★★★ |
| kay | Tick + AttackContext(Pierce/Fan) | 공격 변환 파이프라인 | ★★★ |
| bedivere | OverrideSkillCost + AttackContext.maxTargets + OnAttackHit | 공격 변환 + 스킬비용 | ★★★ |
| tristan | CombatContext.IsMoving + AttackContext(Projectile/bounce) | 이동 컨텍스트 + 공격 변환 | ★★★★ |
| nimue | OnRoomEnter/챕터 경계 + 룬 브리지 | IRuneCovenantBridge | ★★★ |
| leodegrance | Tick + 보급 드롭 | ISupplyDropService | ★★ |
| guinevere | 재선택 브리지 | IReselectionService | ★★★ |

순수 기존 골격으로 가능: **lionel(★)**, 일부 galahad/elaine/leodegrance. 공격 변환 의존: morgana/arthur/kay/bedivere/tristan.

---

## 6. 단계·데이터 구동

- 단계별 수치는 전부 `CovenantDataSO.basicValues/enhancedValues/evolvedValues`(float[]) + `V(idx)/VI(idx)`. 코드엔 매직넘버 금지(fallback만).
- "신능력"(각성 전용 동작)은 `Stage >= CovenantStage.Evolved` 게이트로 코드 분기(기존 규약과 동일).
- 예) lionel: `basic=[연속5, 반경2]`, `enhanced=[연속4, 반경3]`, `evolved=[연속2(재시작), 반경3]`.
- 세이브: 기존 `CovenantSaveEntry{id,stage}` 그대로 — 로스터만 바뀌므로 포맷 무변경. (구버전 id가 세이브에 남아도 `CovenantFactory.Create`가 null→무시되어 안전, 단 출시 전이라 영향 미미.)

---

## 7. 구현 페이즈 (의존도 순)

- **P0 — 배선 기반**: 죽은 훅(OnAttackHit/OnSkillUse/ModifyOutgoing) 디스패치 배선 + 카테고리 필드 + OnWeaponSwap 어댑터 + CombatContext.IsMoving. *코드 변경은 전투/스킬 소유자 합의 후.*
- **P1 — 골격만으로 되는 서약**: lionel, leodegrance(보급 API 동반), galahad(crit 훅 동반), elaine(쿨타임 API 동반). 빠른 검증 루프 확보.
- **P2 — 공격 형태 변환 파이프라인**: `AttackContext`/`AttackShape` + 단일 발사 초크포인트 합의·구축 → kay, arthur, bedivere, morgana.
- **P3 — 고난도/시스템 브리지**: tristan(이동+투사체+튕김), nimue(룬), guinevere(재선택), isolde(교차+스킬변환).
- 각 페이즈 종료 시: 해당 서약 단독 3단계 검증 + 랜덤 3지선다 풀 편입 + 컴파일(`refresh_unity`→`read_console`).

---

## 8. 마이그레이션

| 작업 | 대상 |
|---|---|
| 제거(8) | mordred, morrigan, cuchulainn, lugh, balor, hecate, solomon, prometheus (+ Data SO, Factory 등록, 베이스캠프 픽업 정리) |
| 재작성(4) | galahad, arthur, morgana, nimue (효과를 기획서로 전면 교체) |
| 신규(8) | tristan, lionel, leodegrance, guinevere, elaine, isolde, kay, bedivere |

- 베이스캠프의 `CovenantPickup_Galahad/Arthur/Morrigan` → 신규 로스터에 맞춰 정리(morrigan 제거 → 신규 서약으로 교체하거나 covenantId 비워 전랜덤).
- 삭제 작업은 **사용자 확인 후** 진행(메모리 규약).

---

## 9. 대안 검토 (구조 결정 포인트)

### 공격 형태 변환: (A) 변환 파이프라인 vs (B) 사후 부가효과

| | A. AttackContext 변환 파이프라인 | B. OnAttackHit 사후 side-effect |
|---|---|---|
| 충실도 | 높음(근접→투사체 진짜 변환, 범위/판정 정확) | 낮음(원공격 유지 + 추가 효과만) |
| 침습도 | **높음**(단일 발사 초크포인트 신설 필요) | 낮음(기존 OnAttackHit/DealAoe 패턴 재사용) |
| 적합 서약 | tristan/kay/arthur/bedivere 충실 구현 | lionel/morgana 가산형엔 충분, 변환형엔 부족 |
| 리스크 | 전투 파이프라인 의존·회귀 | tristan "투사체화"를 흉내만 냄(이질감) |

**결정(2026-06-12): A 확정.** 변환형(tristan/kay/arthur/bedivere)은 B로 불가하므로 A가 목표 구조다. 현행엔 통합 발사 초크포인트가 없어(근접 ActAttackState vs 투사체 분리) A는 **공통 발사 seam + shape executor 신규 기능**을 수반한다(§4-3).
- B는 폐기하지 않되 **가산형(lionel 충격파·galahad 폭발류) 한정 임시 수단**으로만 허용 — 변환형엔 사용 금지.
- A 도입 전(P0~P1)엔 변환 의존 서약(P2/P3)을 보류해 일정 분리.

### 카테고리 후보 가중치: 적용 vs 미적용
- 미적용(현행 전랜덤)은 단순하나 동일 카테고리 편중 가능. 적용 시 빌드 다양성↑ 복잡도↑. → **1차는 전랜덤 유지, 데이터(category)만 심어두고 가중치는 후속 옵션.**

---

## 10. 확인 필요 (선결 질문)

1. **로스터 확정**: id 표기(`leodegrance` 등 길이)·12종 전부 1차 범위인지, 아니면 카테고리 단위 점진 도입인지.
2. **전투/스킬 파이프라인 소유권**: P0 훅 배선과 P2 공격 변환 초크포인트를 누가/언제 — 이게 일정의 임계경로.
3. **시스템 의존 일정**: 룬(nimue)·보급(leodegrance)·재선택(guinevere) 브리지 대상 시스템의 현재 상태/여유.
4. **구버전 8종 제거 시점**: 즉시 제거 vs 신규 완성까지 병존.

---

### 부록 — 관련 파일
- 코어: `Assets/RelicFairy/Systems/Covenant/Core/` (Base/Context/Handler/Factory/DataSO/DataTableSO + 4 인터페이스)
- 구현: `Assets/RelicFairy/Systems/Covenant/Implementations/`
- 데이터: `Assets/RelicFairy/Systems/Covenant/Data/`
- 픽업/UI: `CovenantPickup`, `WorldCovenantPickup`, `CovenantChoiceUI`, `UI/Popup/UI_CovenantChoice`
- 디스패치 지점(현행): `MonsterBase`(OnKill), `PlayerController`(OnTakeDamage/Tick/TryPreventDeath), `RunFlowController`(OnRoomEnter), `RoomClearController`(OnRoomClear)
