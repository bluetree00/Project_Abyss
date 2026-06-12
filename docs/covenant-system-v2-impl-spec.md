# 서약 시스템 v2 — 구현 설계서 (Implementation Spec)

> 작성일: 2026-06-12 / 상위 문서: `docs/covenant-system-v2-design.md` (계획)
> 본 문서는 **코드로 옮길 수 있는 수준의 구체 스펙**. 페이즈별 착수 시 이 문서를 기준으로 한다.
> 원칙: 기존 골격 재사용, 증분 확장, 수치 전부 데이터 구동, 신규 시스템 의존은 브리지로 격리.

---

## 1. 통합 지점 맵 (확정)

| 목적 | 파일 | 지점 |
|---|---|---|
| 공격 변환 진입 | `Characters/Player/Scripts/WeaponEffectHandler.cs` | `PlayEffect()` 스폰 루프 + `SetupColliderInstance`/`SpawnFanShots`/`SpawnExtraShots` |
| 근접 적중·피해 | `Weapon/Scripts/ColliderInstance.cs` | `ApplyDamage()` |
| 투사체 적중·피해 | `Shared/Characters/Weapon/BasicArrow.cs` | 피해 적용부 |
| 치명타/최소피해 | `Systems/Combat/CombatCalculator.cs` | `RollCrit(weapon, baseDamage, out isCrit)` (근접·투사체 공유) |
| 스킬 사용 | `Shared/Characters/PlayerState/LayerFSM/ActState/ActSkillStateBase.cs` | Enter 직후 |
| 무기 교체 | `Shared/Characters/Weapon/PlayerWeaponManager.cs` | `OnWeaponChanged` 이벤트 |
| 방 입장/클리어 | `RunFlowController`(OnRoomEnter) / `RoomClearController`(OnRoomClear) | 이미 배선됨 |
| 이펙트=판정 결합 | `Shared/Effects/EffectBehaviour.cs` + `ColliderInstance` | **Combined(같은 GO) → transform 상속으로 자동 동기화** |

---

## 2. 신규·변경 타입 카탈로그

### 2-1. 카테고리 메타데이터
```csharp
// 신규 Core/CovenantCategory.cs
public enum CovenantCategory { ActionConditional, RunStructure, CombatRhythm, Tradeoff }
```
- `CovenantDataSO`에 `[SerializeField] public CovenantCategory category;` 추가.
- `CovenantBase`에 `public virtual CovenantCategory Category => Data?.category ?? CovenantCategory.ActionConditional;`
- 용도: 선택 UI 색/태그. (카테고리 가중치 추첨은 1차 보류 — 데이터만 심어둠)

### 2-2. 죽은 이벤트 훅 배선 (P0, 코드 변경 = 전투 소유자 합의)
인터페이스 변경 없음. **디스패치만 추가**:
- `ColliderInstance.ApplyDamage`: `TakeDamage` **직전** `dmg = covHandler.ModifyOutgoing(dmg, ctx)`, **직후** `covHandler.OnAttackHit(target, finalDmg)`.
- `BasicArrow`(투사체): 동일 2지점 추가.
- `ActSkillStateBase` Enter: 기존 `EffectManager.OnSkillUse(Slot)` 옆에 `…Run?.CovenantHandler?.OnSkillUse(Slot)`.
- 접근자: `GameRunBootstrapper.Instance?.Run?.CovenantHandler` (기존 패턴과 동일).

### 2-3. 신규 이벤트
```csharp
// ICovenantEventListener에 default no-op 추가 → CovenantBase virtual no-op → Handler 디스패치
void OnWeaponSwap(WeaponData prev, WeaponData next);
```
- 발화: `CovenantHandler`가 `BindPlayer` 시 `WeaponManager.OnWeaponChanged` 구독 → 이전 무기 캐싱 후 `OnWeaponSwap(prev, next)` 호출, `Cleanup`에서 해제.
- 이동 컨텍스트: **신규 이벤트 대신** `CombatContext`에 `bool IsMoving; Vector3 MoveDir;` 필드 추가 → 공격 산출 시 `PlayerController` 상태 스냅샷. (tristan이 읽음)

### 2-4. 공격 형태 변환 (A — Core 신규)
```csharp
// 신규 Core/AttackRequest.cs
public enum AttackShape { Default, Projectile, Pierce, Fan }

public struct AttackRequest {
    public Vector3   origin, dir;
    public float     baseMultiplier;
    public int       hitCount;      // 같은 타격 판정 ×N (기본 1)
    public float     radiusScale;   // 콜라이더/이펙트 동시 배율 (기본 1)
    public int       maxTargets;    // 동시 타격 상한 (0=무제한)
    public AttackShape shape;       // 기본 Default
    public int       bounceCount;   // 적중 후 튕김 (기본 0)
    public bool      isMovingAttack;
    public WeaponData prevWeapon;   // morgana 잔상 참조
}

// 신규 인터페이스 ICovenantAttackModifier
void ModifyAttack(ref AttackRequest req);
// CovenantBase virtual no-op + CovenantHandler.ModifyAttack(ref req) 순회
```
**통합:** `WeaponEffectHandler.PlayEffect` 스폰 직전 `AttackRequest` 구성 → `CovenantHandler.ModifyAttack(ref req)` → 결과를 스폰 파라미터에 반영:
- `radiusScale` → Combined 오브젝트 `transform.localScale` / Spawned 콜라이더 `sizeMultiplier`.
- `hitCount` → `attackId` 분리 또는 재활성.
- `shape=Fan` → 기존 `SpawnFanShots` 재사용. `shape=Projectile/Pierce` → 투사체 스폰 경로 + **전용 변형 프리팹**(arrow처럼 데이터 지정).
- `maxTargets` → `ColliderInstance` dedup/캡.
- **규약: 변환형은 반드시 Combined(이펙트+콜라이더 동일 GO) 프리팹** → transform 상속으로 판정 자동 동기화(§1).

### 2-5. 치명타/최소피해 훅 (galahad)
`CombatCalculator.RollCrit` 확장 — 서약 질의 추가:
```csharp
// 의사코드
var cov = GameRunBootstrapper.Instance?.Run?.CovenantHandler;
if (cov != null && cov.TryGetCritOverride(out float critChance, out float minFloorRatio)) {
    // critChance=0 고정 시 비치명타, baseDamage에 최소피해 하한(최대피해×minFloorRatio) 적용
}
```
- `CovenantHandler`에 `bool TryGetCritOverride(out float chance, out float minFloorRatio)` 추가(galahad만 응답).
- 확정 치명타 주기(연속 N회)는 galahad가 `OnAttackHit` 카운터로 관리 → 해당 타격만 강제 치명타 플래그.

### 2-6. 시스템 브리지 (의존 격리, no-op 폴백)
```csharp
public interface IRuneCovenantBridge { void GrantBlock(int count); void LowerThreshold(int stageTier, float toPercent); }  // nimue
public interface ISupplyDropService  { void DropSupply(int tier, Vector3 at); }                                            // leodegrance
public interface IReselectionService { void GrantReroll(int count, int qualityBonus); }                                    // guinevere
```
- `CovenantContext`에 옵셔널 참조로 주입(없으면 null). 서약은 null이면 무동작 → 런 진행 무영향.

---

## 3. 데이터 스키마 — `CovenantDataSO` 값 배열 (PDF 수치)

각 서약 `basicValues/enhancedValues/evolvedValues` 인덱스 규약. (코드는 `V(idx)/VI(idx)`로만 접근, 매직넘버 금지)

| 서약 | idx 규약 | Basic(CH1) | Enhanced(CH2) | Evolved(CH3) |
|---|---|---|---|---|
| morgana | [0]지속 [1]잔상배율 [2]잔상수 | 5, 0.40, 1 | 7, 0.60, 1 | 7, 0.60(+0.40), 2 |
| tristan | [0]튕김 [1]최대크기배 | 0, 1 | 1, 1 | 1, 3 |
| lionel | [0]연속수 [1]반경 [2]재시작카운터 | 5, 2, 0 | 4, 3, 0 | 2, 3, 2 |
| leodegrance | [0]주기초 [1]아이템티어 [2]보스직전보장 | 60, 1, 0 | 45, 2, 0 | 30, 3, 1 |
| nimue | [0]블록/챕터 [1]1단계% [2]2단계% [3]3단계% [4]자동채움칸 | 1, 22, 50, 70, 0 | 2, 22, 40, 58, 0 | 2, 22, 40, 58, 1 |
| guinevere | [0]재선택수 [1]등급상향 [2]복제 | 1, 0, 0 | 2, 1, 0 | 3, 1, 1 |
| elaine | [0]창초 [1]쿨단축 [2]최대스택 [3]보너스임계 [4]보너스단축 | 3, 0.5, 3, 0, 0 | 3, 0.7, 3, 0, 0 | 3, 0.7, 3, 5, 1 |
| isolde | [0]교차카운트 [1]범위증가 [2]연속발동 | 3, 0, 1 | 2, 0.5, 1 | 2, 0.5, 2 |
| kay | [0]주기초 [1]형태(0관통1부채꼴) [2]적중당단축 | 8, 0, 0 | 6, 1, 0 | 6, 1, 0.5 |
| bedivere | [0]쿨증가 [1]동시타겟 [2]주기치 [3]주기효과 | 0.5, 2, 0, 0 | 0.5, 3, 20, 2 | 0.5, 3, 30, 1 |
| galahad | [0]치명타고정 [1]최소피해비 [2]확정주기 [3]최대고정창 | 0, 0.80, 0, 0 | 0, 0.90, 20, 0 | 0, 0.90, 15, 3 |
| arthur | [0]범위배율 [1]판정횟수 [2]연속임계 [3]디버프/낙인 | 0.5, 2, 0, 0 | 0.5, 3, 10, 0.5 | 0.5, 3, 15, 1 |

> 표기 모호한 칸(예: morgana 각성 2잔상의 개별배율)은 해당 서약 구현 페이즈에서 PDF 재확인 후 확정.

---

## 4. 페이즈별 작업 (체크리스트 + verify)

### P0 — 기반 배선 (전투 소유자 합의 후)
- [ ] `CovenantCategory` + `CovenantDataSO.category` + `Base.Category`
- [ ] 죽은 훅 배선: `OnAttackHit`/`ModifyOutgoing`(ColliderInstance+BasicArrow), `OnSkillUse`(ActSkillStateBase)
- [ ] `OnWeaponSwap` 인터페이스+어댑터, `CombatContext.IsMoving/MoveDir`
- verify: 더미 서약으로 각 훅 로그 1회 확인 + `refresh_unity`→`read_console` 0 에러

### P1 — 골격만으로 되는 서약
- [ ] **lionel**(OnAttackHit 타깃별 연속 카운터 + 기존 DealAoe) ★최우선 검증
- [ ] **galahad**(RollCrit 훅 + 최소피해 하한 + 확정치명타 카운터)
- [ ] **leodegrance**(Tick + `ISupplyDropService`)
- [ ] **elaine**(OnSkillUse+OnAttackHit + 쿨단축 API)
- verify: 각 서약 단독 3단계(선택/강화/각성) 동작 + 랜덤 풀 편입

### P2 — 공격 변환 파이프라인 + 변환형
- [ ] `AttackRequest`/`AttackShape`/`ICovenantAttackModifier`/`Handler.ModifyAttack`
- [ ] `WeaponEffectHandler.PlayEffect` 통합(구성→ModifyAttack→스폰 반영)
- [ ] **arthur**(radiusScale+hitCount) → **kay**(Fan/Pierce) → **bedivere**(maxTargets) → **morgana**(OnWeaponSwap+잔상 hitCount)
- verify: Combined 프리팹로 판정·이펙트 동기 확인, 형태별 시각 프리팹 배선

### P3 — 고난도/시스템 브리지
- [ ] **tristan**(IsMoving + Projectile 변환 + bounce)
- [ ] **nimue**(`IRuneCovenantBridge`)
- [ ] **guinevere**(`IReselectionService`)
- [ ] **isolde**(교차 시퀀스 + `ModifySkillEffect` 광역화)

---

## 5. 마이그레이션

| 작업 | 대상 | 비고 |
|---|---|---|
| 제거(8) | mordred, morrigan, cuchulainn, lugh, balor, hecate, solomon, prometheus | `Implementations/*.cs`, `Data/CovenantData_*.asset`, `CovenantFactory` 등록, 베이스캠프 픽업 — **삭제는 사용자 확인 후** |
| 재작성(4) | galahad, arthur, morgana, nimue | 효과 전면 교체, id 유지 |
| 신규(8) | tristan, lionel, leodegrance, guinevere, elaine, isolde, kay, bedivere | `Implementations/*.cs` + `Data/CovenantData_*.asset` + `CovenantFactory` 등록 |

- `CovenantFactory`: id 상수 + `_registry` 12종으로 교체. `AllIds`가 곧 랜덤 3지선다 풀.
- 베이스캠프 픽업 `CovenantPickup_Galahad/Arthur/Morrigan`: 신규 로스터로 정리(morrigan 제거 → 신규 id로 교체하거나 `covenantId` 비워 전랜덤).
- 데이터 생성: 기존 `Editor/CovenantDataGenerator.cs` 재사용/확장.

---

## 6. 세이브/복원 (무변경)
- `CovenantSaveEntry{id, stage}` 그대로. 로스터만 변경.
- 구버전 id가 세이브에 남아도 `CovenantFactory.Create`가 null→무시(안전). 출시 전이라 영향 미미.
- 복원 경로: `GameRunBootstrapper` → `CovenantHandler.RestoreSelections` (기존).

---

## 7. 리스크 / 소유권 / 오픈

1. **전투 파이프라인 소유권(임계경로)**: P0 훅 배선, P2 `WeaponEffectHandler`/`CombatCalculator` 변경은 전투 코드 영역 — 충돌·회귀 주의.
2. **변환형 시각 자산**: tristan(무기 파편 투사체)·kay(관통 빔) 전용 프리팹 authoring 필요(런타임 변신 불가).
3. **시스템 브리지 일정**: 룬(nimue)/보급(leodegrance)/재선택(guinevere) 대상 시스템 현황 확인 필요. 미구현 시 해당 서약 no-op로 안전 출시 가능.
4. **데이터 모호 수치**: §3 표의 일부 칸은 PDF 재확인 후 확정.

---

## 8. 디렉토리·네이밍 규약
- 구현체: `Systems/Covenant/Implementations/{Name}Covenant.cs`, `CovenantId => CovenantFactory.{Name}`
- 데이터: `Systems/Covenant/Data/CovenantData_{id}.asset`
- 신규 Core: `Systems/Covenant/Core/{Type}.cs`
- 브리지 구현은 각 시스템 소유 폴더에, 인터페이스만 `Covenant/Core/`.
- 단계 차등: 수치는 `V/VI`, 신능력은 `Stage >= Enhanced/Evolved` 게이트(기존 규약).
