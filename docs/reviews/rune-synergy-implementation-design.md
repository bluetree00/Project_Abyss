# 멀린 룬 속성 시너지 구현 설계 (24효과 = 6속성 × 4단계)

> 작성일 2026-06-13 · 문서(설계)만, 게임플레이 코드 미구현 · 커밋/push 안 함
> 원본 스펙: 바탕화면 `멀린룬 시너지 .txt` (= `docs/merlin-rune-elements-design.md` 와 동일 기획)
> 본 문서 목적: 스펙을 **현재 룬 코드 구조에 매핑** — "무엇을 어디에 채우는가 + 신규 공용 시스템 + 훅 배선 갭 + 단계별 우선순위"

---

## 0. 한눈 요약 (TL;DR)

- **연결 골격은 이미 존재**한다. `RuneEffectDispatcher`(per-Player) ↔ `IRuneEffect`/`RuneEffect`(빈 훅) ↔ `RuneEffectFactory`(현재 전부 빈 효과 반환=STUB) ↔ `MerlinRuneBridge.ApplyMechanicEffect`(단계 도달 시 `player.RuneEffects.Activate(entry)`). **24효과는 이 골격 위에 `RuneEffect` 서브클래스 24개 + 팩토리 분기만 채우면 된다.**
- **단계 판정 모델이 스펙과 불일치**한다. 코드는 `clusterSize >= entry.threshold`(최대 연결 클러스터 셀 수). 스펙은 `점유율(채움%) >= 30/50/70/100%`. → **판정 소스 교체 필요** (데이터는 이미 `GetZoneOccupiedCounts`/`GetZoneTotalCounts` 둘 다 존재).
- **존 정의가 두 갈래로 갈라져 있다.** 코드(`ElementDef`)는 이미 6속성(F/I/T/P/L/D)으로 피벗됐으나, **실제 차트(`MERLIN_RUNE_SYNERGY_DATA.csv`, `MERLIN_RUNE_ZONE_MAP.csv`)와 `SynergyMechanicsState`/`ApplySynergyMechanicEffect`는 아직 구(舊) 스탯존(ATK/MAG/DEF/SPD/HP/LUCK)이다.** 데이터·구 메커닉을 속성 스킴으로 재작성해야 한다.
- **상태이상/장판/리소스 인프라가 사실상 0.** 아이템 Poison/Freeze/Petrify/Stun은 전부 `Debug.Log` 스텁("TODO: 상태이상 시스템 연결"). 화상만 `MonsterBurnHandler`로 실재. → **통합 StatusEffect 시스템 + 장판(필드) 시스템 + 플레이어 리소스(스택/게이지) 컨테이너 신규.**
- **훅 배선 2개가 끊겨 있다.** ① 플레이어 피격(`OnDamaged`)은 몬스터가 `RaiseHit`를 안 타고 `player.TakeDamage` 직접 호출이라 **절대 발화 안 함**(→ 어둠 속성 트리거 불능). ② 원거리(활/투사체) 적중은 `BasicArrow`가 `HitFeelService.Hit`만 호출하고 `RaiseHit`를 안 타서 **OnHit/OnCrit 미발화**. → 룬 OnHit/OnCrit·OnDamaged는 `HitFeedbackService.OnHit`이 아니라 **근/원 공통 경로(`EffectManager.OnPostDealDamage`) + `PlayerController.TakeDamage`** 에 다시 매달아야 한다.

---

## 1. 현재 룬 아키텍처 정독 (설계 전제)

### 1.1 데이터 로딩·모델
| 항목 | 위치 | 메모 |
|---|---|---|
| 행 모델 | `RuneSynergyEntry` — `BlockEntry.cs:50` | `zone_id, zone_name, threshold(int), effect_type, trigger, value, value2, value3, max_stack, duration, description, stat_version` |
| 로더 | `RuneDataManager` — `BlockDataManager.cs` | CDN `MERLIN_RUNE_SYNERGY_DATA` → 실패 시 Addressables `MERLIN_RUNE_SYNERGY_DATA` TextAsset 폴백. `zone_id`로 그룹핑(`_synergyByZone`) |
| 조회 | `GetZoneSynergies(zoneId)` = `GetGrid(zoneId)` | zone당 `List<RuneSynergyEntry>` (단계 여러 행) |
| 존맵 | `MERLIN_RUNE_ZONE_MAP` → `RuneZoneMapEntry{hex_row,pattern}` | `pattern` 문자열의 각 char = 존 코드. `GetZoneCellPositions(zoneId)` 가 `ElementDef.IdToCode(zoneId)` 로 셀 좌표 추출 |

> ⚠ 현재 desktop CSV 실측: `SYNERGY`/`ZONE_MAP` 모두 **구 스탯존(A/M/D/S/H/L)** 코드. `ElementDef` 코드는 **F/I/T/P/L/D**. (`L`/`D`는 양쪽에 다른 의미로 충돌). 데이터 전면 재작성 필요.

### 1.2 존 정의 (이미 속성으로 피벗)
`ElementDef.cs` — 단일 정의 테이블. 6속성 `Order` = FIRE/ICE/ELECTRIC/GRASS/LIGHT/DARK, 코드 `F/I/T/P/L/D`, `'+'`=CENTER(시너지 제외, 보너스 전용). 색/아이콘/표시명 포함. **신규 효과·CSV·UI 모두 이 테이블을 단일 출처로 써야 한다(중복 금지).**

### 1.3 충전율(채움%) 산출 위치 — 단계 판정 소스
- `MerlinRuneHexGridView`:
  - `GetZoneOccupiedCounts()` → zone_id별 **점유 셀 수** (`MerlinRuneHexGridView.cs:246`)
  - `GetZoneTotalCounts()` → zone_id별 **전체 셀 수** (`:272`)
  - `RefreshPlacedCells()` 등이 `OnZoneCellsUpdated(zoneCounts, clusterSizes)` 호출. **`clusterSizes` = `ZoneClusterCalculator.Compute`(BFS 4방향 최대 연결 클러스터)** (`:184`)
- `MerlinRuneBridge.CheckAndApplyThresholds(clusterSizes)` 가 **`clusterSize >= entry.threshold`** 로 단계 판정 (`BlockSynergyBridge.cs:630`). → **스펙의 채움% 모델과 다름.** (`GetZoneTotalCounts`는 현재 단계 판정에 미사용.)

### 1.4 효과 활성 경로
```
MerlinRuneHexGridView.OnZoneCellsUpdated
  → MerlinRuneBridge.CheckAndApplyThresholds (zone별 applied HashSet<int>로 1회성 보장)
    → ApplyMechanicEffect(zoneId, entry)        // BlockSynergyBridge.cs:671
        ├─ RuntimeStats.ApplySynergyMechanicEffect(entry)   // 구 스탯존 메커닉(레거시)
        └─ player.RuneEffects.Activate(entry)               // 신 속성 효과 진입점 ★
```
- `RuneEffectDispatcher.Activate(entry)` (`RuneEffectDispatcher.cs:32`): `effect_type` 중복 방지 → `RuneEffectFactory.Create(entry)` → `_active`에 추가 → `OnActivate(player)`.
- `RuneEffectFactory.Create` (`RuneEffectFactory.cs:10`): **현재 effect_type 무관 항상 빈 `RuneEffect` 반환 (STUB).**

### 1.5 전투 훅 디스패치 현황
| 훅 | 디스패처 소스 | 발화 경로 | 상태 |
|---|---|---|---|
| `Tick(dt)` | `PlayerController.Update` → `_runeEffects.Tick` (`PlayerController.cs:602`) | 매 프레임 | ✅ |
| `OnSkillUsed` | `ActSkillStateBase.cs:64` → `RuneEffects.NotifySkillUsed()` | 스킬 시전 | ✅ |
| `OnKill` | `QuestEvents.OnMonsterKilled` → `HandleKill` (`RuneEffectDispatcher.cs:76`) | 처치 | ⚠ codeName만 전달 — **위치/타겟 좌표 없음** (장판/광역 처치 트리거엔 부족) |
| `OnHit`/`OnCrit` | `HitFeedbackService.OnHit` 구독 → `HandleHit` (`:82`) | `ColliderInstance.RaiseHit` (`ColliderInstance.cs:165`) | ⚠ **근접/이펙트 공격만.** 원거리는 미발화(아래 1.6) |
| `OnDamaged` | 동일 `HandleHit`에서 target==player 분기 (`:88`) | — | ❌ **발화 안 됨**(아래 1.6) |

### 1.6 끊긴 배선 2건 (핵심 갭)
- **플레이어 피격 → `OnDamaged` 불능**: 몬스터 공격은 `RaiseHit`를 타지 않고 `player.TakeDamage`를 직접 호출(`HitFxPresenter.cs:7` 주석 + `PlayerController.TakeDamage` `:95` 경로). `HitFeedbackService.OnHit`은 발화하지 않으므로 디스패처의 target==player 분기는 죽은 코드. → **어둠(피격 시 게이지) 전체가 트리거 불능.**
- **원거리 적중 → `OnHit`/`OnCrit` 불능**: `BasicArrow`는 적중 시 `HitFeelService.Hit(...)`만 호출(`BasicArrow.cs:168`), `RaiseHit` 미호출. → 활/투사체 빌드에서 불·얼음·전기·빛 등 OnHit 의존 효과 전부 미발동.
- **공통점**: 근접(`ColliderInstance`)·원거리(`BasicArrow`) **둘 다** `mgr.OnPostDealDamage(report)`(=`ItemEffectManager`)는 호출한다. → 룬 OnHit/OnCrit의 **신뢰 가능한 단일 경로는 `EffectManager.OnPostDealDamage`**.

### 1.7 스탯 적용 가능 표면 (`PlayerRuntimeStats`)
- 합산 레이어 존재(item/room/covenant/relic/synergy…). `ApplySynergyEffect(effectType,value)`는 **flat 스탯**만 처리(Melee/Ranged/Defense/MaxHp/Luck/AttackSpeed/Lifesteal/CDR). `SetBonusAttackSpeed(bonus)` 존재.
- **크릿 확률/피해**: 읽기 전용 `CritChanceBonus`=`_relicCritChance+_buffCritChance`. 외부 주입은 `SetRelicCritBuff(chance,damage)` **단일 슬롯**(가웨인 정오 버프가 점유) → 빛 속성(광채 스택 → 치확)과 **충돌**.
- **캐릭터 배율**: `SetCharacterAttackMultiplier`/`SetCharacterDefenseBonus` 도 **단일 소유자**(유물 메커닉 점유) → 어둠(공+25%)·작열 등과 **충돌**.
- 결론: 속성 리소스 기반 동적 버프(어둠 게이지→공%, 빛 스택→치확, 전기 스택→공속)는 **기존 단일 슬롯을 재사용하면 안 됨 → 시너지 전용 신규 레이어 필요.**

### 1.8 기존 상태이상/DoT 인프라 (재사용 평가)
| 자산 | 위치 | 재사용성 |
|---|---|---|
| 화상 DoT | `MonsterBurnHandler` (`Shared/Combat/MonsterBurnHandler.cs`) | ✅ 컴포넌트-부착 DoT 패턴의 레퍼런스. 점화/독으로 일반화 가능 |
| 받는 피해 증폭/감소 | `MonsterBase.ApplyDamageTakenAmp(amp,dur)` + `_incomingDamageMulti`/`_debuffDamageTakenMult` (`MonsterBase.cs:564`) | ✅ 빙결 분쇄(피해+30%)·방어-20%·풀 보스받피+15%에 직접 활용 |
| 아이템 상태이상 | `PoisonOnHitEffect`/`FreezeEffect`/`PetrifyEffect`/`StunEffect` (`OnHitEffects.cs`) | ❌ 전부 VFX+`Debug.Log` 스텁. **신규 StatusEffect로 흡수 후 이 스텁들도 이관 권장** |
| 몹 이동속도 | `MonsterBase._agent.speed`/`_baseAgentSpeed` (`:236`) | ⚠ public slow setter 없음 → 신규 |
| 무력화 상태 | `SpecialStateConstraint.Invincible/UnInterruptible`, `GetHitState` | ⚠ stun/freeze 전용 상태 없음 → 신규(제약 추가 또는 status 컴포넌트가 agent/FSM 정지) |
| 광역 타겟 질의 | `Physics.OverlapSphere` + `IDamageable` 필터 (`PlayerController.cs:1360` 에임어시스트) | ✅ 체인/충격파/빙하/원뿔 타겟 질의의 검증된 패턴 |
| 구 메커닉 플래그 | `SynergyMechanicsState` + `ApplySynergyMechanicEffect` | ⚠ ATK/MAG/DEF/SPD/HP/LUCK 전용. **실행부도 대부분 미구현(스텁).** 속성 스킴엔 부적합 → 신 효과는 `RuneEffect` 경로로 분리 |

---

## 2. 스펙 → 코드 매핑 (6속성 × 4단계, 누적형)

판정: **`fill% = occupied/total`** 이 `{0.30, 0.50, 0.70, 1.00}` 임계 도달 시 해당 단계 효과 활성. **누적(상위 도달 시 하위 유지).** 모든 "공격력 N%"는 명시 없으면 **무기 타입별 유효 공격력**(`GetEffectiveAttack`) 기준으로 해석(리스크 §5).

범례 — 훅: H=OnHit, C=OnCrit, K=OnKill(+위치 필요), D=OnDamaged, S=OnSkillUsed, T=Tick, A=OnActivate.
신규 시스템: **ST**=StatusEffect, **FD**=장판/필드, **RS**=플레이어 리소스(스택/게이지), **AQ**=광역/체인 타겟질의, **DM**=즉발 피해 경로, **SL**=신규 스탯 레이어.

### 🔥 불 Fire — 화염 증폭 (자기완결, 적 상태=점화)
| 단계 | 효과 | 훅 | 신규 |
|---|---|---|---|
| 1 잔불(30%) | 적중 시 공격력 8% 화염 추가 | H | DM |
| 2 점화(50%) | 잔불 5회마다 점화(3초, 초당 공16%×3) | H+RS(카운터) | ST(점화 DoT=Burn 일반화), RS |
| 3 폭염(70%) | 점화 상태 적 공격 시 화염 피해 +35% | H | ST(상태조회) |
| 4 작열(100%) | 점화 만료 시 폭발 = 남은 DoT 합산 × 공150% 광역 | (ST 만료 콜백) | ST(만료 이벤트+잔여DoT추적), AQ, DM |

### ❄ 얼음 Ice — 제어 (적 상태=서리/빙결)
| 단계 | 효과 | 훅 | 신규 |
|---|---|---|---|
| 1 서리(30%) | 적중 시 이속-12%/3초, 최대 -24%(2중첩) | H | ST(슬로우, 몹 agent.speed), 슬로우 setter |
| 2 빙결(50%) | 서리 2중첩에 **스킬** 적중 시 빙결 1.5초(내부쿨 10) | S 또는 H(스킬 적중 식별) | ST(빙결=정지), 내부쿨 RS |
| 3 분쇄(70%) | 빙결 적 피해 +30%, 빙결 해제 시 방어-20%/5초 | H + ST 해제 콜백 | ST, `ApplyDamageTakenAmp` 재사용 |
| 4 빙하연쇄(100%) | 빙결 해제 시 공150% 광역 + 서리 1중첩 | (ST 해제 콜백) | AQ, DM, ST |

### ⚡ 전기 Electric — 자기강화 루프 (단독완결, 의존도 최저)
| 단계 | 효과 | 훅 | 신규 |
|---|---|---|---|
| 1 정전기(30%) | 공속+10%, 적중 시 스택+1(스택당 공속+1%,10초,최대10) | A(기본+10%)+H(스택)+T(만료) | RS(스택), **SL(공속)** |
| 2 방전(50%) | 스킬 시 스택 전량 소모 = 스택당 공4% 번개 즉발 | S | DM, RS |
| 3 감전(70%) | 방전 시 감전(1초 마비, 스택당 +지속 최대3초), 감전 중 공속+8% | S(방전 연동)+T | ST(감전=마비), SL |
| 4 과부하(100%) | 공격 시 체인 라이트닝(주변 2명 50%, 없으면 같은적 2회) | H | AQ, DM |

### ☘ 풀 Grass — 독안개 장판 (필드 중심, 인프라 최대)
| 단계 | 효과 | 훅 | 신규 |
|---|---|---|---|
| 1 살포(30%) | 적중 자리 독안개 10초(최대3), 내부 초당 공4% 독 | H | **FD(장판 풀링·체류판정·DoT)**, ST(독) |
| 2 강화(50%) | 장판 크기+50%, 겹침 영역 독피해+50% | (FD 파라미터) | FD(겹침 판정) |
| 3 간파(70%) | 크기+50%, 독피해 시 최대HP0.1% 회복, 겹침 3초+ 적 공-20% | (FD) | FD(체류 타이머), 몹 공격력 디버프(신규), 플레이어 회복 |
| 4 독무지배(100%) | 장판끼리 닿으면 전부 겹침 판정, 독×2, 회복0.2%, 집중분산30%, **보스 받피+15%** | (FD) | FD(연결 병합), 보스 식별+`ApplyDamageTakenAmp` |

### ✦ 빛 Light — 치명타 루프 (리소스+장판)
| 단계 | 효과 | 훅 | 신규 |
|---|---|---|---|
| 1 광채(30%) | 치명 시 스택+1(최대10), 스택당 치확+1% | C | RS(스택), **SL(치확)** |
| 2 광폭발(50%) | 10스택 소모 = 전방 원뿔 공150% 폭발, 초기화 | (RS 임계 자동/H) | AQ(원뿔), DM, RS |
| 3 성역(70%) | 광폭발 자리 빛장판 3초(최대1), 장판 위 치명 시 광채+3 | (FD)+C | FD(추적X, 고정), 위치=폭발 지점 |
| 4 빛의장판(100%) | 장판이 캐릭터 추적, 장판 위 공격 치확+20%·치피+30% | T(추적)+H/C | FD(플레이어 추적), SL(치확/치피) |

### 🌑 어둠 Dark — 피격강화 (게이지, OnDamaged 의존 → 배선 갭 직격)
| 단계 | 효과 | 훅 | 신규 |
|---|---|---|---|
| 1 잠식(30%) | 피격 +10·적중 +3(최대100), 게이지 비례 공 최대+15% | **D**+H | RS(게이지), **SL(공%)**, ⚠배선 §3 |
| 2 암흑해방(50%) | 게이지 100 시 5초 암흑(공+25%·받피-15%) | (RS 임계)+T | SL(공%/받피), 타이머 |
| 3 그림자잔상(70%) | 암흑 중 적중 시 동일공격 40%를 0.5초 후 추가 | H+T(지연큐) | DM, 지연 스케줄러 |
| 4 심연각성(100%) | 게이지 달성 시 랜덤 버프 풀에서 1개 | (RS 임계) | 랜덤 버프 풀(데이터+적용), SL |

**어둠4 랜덤 버프 풀**(7종×단계값): 공격력/공속/쿨감/모든스탯/체력회복/주변몹방어감소/받피감소. 대부분 기존 `ApplySynergyEffect` 계열로 흡수 가능하나 "모든 스탯 %"·"주변 몹 방어 감소"는 신규 처리 필요.

---

## 3. 설계 산출

### 3.1 데이터 모델

**원칙: 스키마 최소 변경 + 의미만 재정의.** 기존 `RuneSynergyEntry`/CDN 차트/뒤끝 flat 구조를 유지한다.

#### (a) effect_type 키 = (속성, 단계) 고유 키 24종
팩토리 분기의 키. 예약 네이밍(영문 PascalCase, `ElementDef` Id 접두):
```
FireEmber / FireIgnite / FireBlaze / FireScorch
IceFrost / IceFreeze / IceShatter / IceGlacialChain
ElectricStatic / ElectricDischarge / ElectricShock / ElectricOverload
GrassMist / GrassMistAmp / GrassMistInsight / GrassMistDominion
LightRadiance / LightBurst / LightSanctum / LightField
DarkErosion / DarkUnleash / DarkAfterimage / DarkAbyssAwaken
```

#### (b) 단계 임계 = 채움% (threshold 의미 재정의)
- `RuneSynergyEntry.threshold(int)` 를 **클러스터 셀 수가 아니라 백분율(30/50/70/100)** 로 재해석.
- 판정식 변경(설계): `MerlinRuneBridge`가 `clusterSizes` 대신 **`fillPct = occupied/total`** 로 비교 →
  `if (fillPct * 100f >= entry.threshold)`.
- 데이터 소스: `OnZoneCellsUpdated(zoneCounts, ...)` 에 이미 들어오는 `zoneCounts`(=occupied) + `GetZoneTotalCounts()`(=total). **`OnZoneCellsUpdated` 시그니처에 totals를 추가로 넘기거나, 브리지가 `MerlinRuneHexGridView`에서 직접 totals를 조회**(둘 중 후자가 시그니처 영향 최소).
- 라이브 재계산: 배치/제거 시 매번 fill% 재평가하되, **하강 시 단계 효과 비활성**까지 지원하려면 현재 1회성 `_appliedThresholds(HashSet<int>)` 를 **"현재 활성 최고 단계" 상태값**으로 바꿔 `Activate`/`Deactivate` 양방향 호출(현재 `Deactivate` 미사용). (편집 중 단계 변동 UX = 기획 확인 필요, §5)

#### (c) 파라미터 인코딩 (value/value2/value3 + max_stack + duration 초과분)
효과당 파라미터가 3슬롯을 초과(예: 점화 = 발동주기5 / 지속3초 / 틱당16% / 틱3회). 두 안:
- **A안(권장, 스키마 무변경)**: `value/value2/value3/max_stack/duration` 5필드를 효과별 **위치 규약(positional schema)** 으로 사용 + 부족분은 **상수로 코드 내 기본값**(스펙이 고정 수치이므로 밸런싱 전까지 충분). 각 `RuneEffect` 서브클래스 상단에 "이 효과의 필드 매핑" 주석 표.
  - 예) `FireIgnite`: value=틱당%(0.16), value2=틱간격(1), max_stack=틱수(3), duration=점화지속(3), value3=발동주기(5).
- **B안(확장)**: `description` 옆에 `params_json` 한 칼럼 추가 → `Dictionary<string,float>` 파싱. 유연하나 뒤끝 차트·로더·세이브(`SynergyRecord`) 동시 변경 필요. **출시 전 밸런싱이 잦으면 B안 고려, 현 시점 A안.**

#### (d) RuneEffectFactory 매핑 방식
```
// 현재: 항상 빈 RuneEffect (STUB)
// 설계: effect_type → 전용 서브클래스 (switch 또는 static Dictionary<string,Func<RuneEffect>>)
switch (entry.effect_type) {
  case "FireEmber":  fx = new FireEmberEffect(); break;
  ... 24종 ...
  default: fx = new RuneEffect(); // 미구현 단계는 안전한 빈 효과
}
fx.Bind(entry); return fx;
```
- `Bind(entry)` 로 파라미터 주입(이미 존재). 미구현 effect_type은 빈 효과로 폴백 → 점진 구현 안전.

#### (e) CSV/SO 재작성
- `MERLIN_RUNE_SYNERGY_DATA`: 구 12행(스탯존) → **24행(6속성×4단계)**. 컬럼 그대로, `zone_id`=ElementDef Id, `threshold`=30/50/70/100, `effect_type`=위 24키, value*~duration=positional.
- `MERLIN_RUNE_ZONE_MAP`: `pattern` 의 존 코드 문자를 **F/I/T/P/L/D/+** 로 재작성(현재 A/M/D/S/H/L). 채움% 분모가 여기서 나오므로 **속성별 셀 배분(총 셀 수)이 밸런스 핵심**.
- 폴백: `RuneDataManager`가 CDN 실패 시 Addressables TextAsset 사용 — **동일 24행 JSON을 Addressables에도 갱신**(`reference_unity_execute_code_broken` 참고: 그룹 .asset 수동/force refresh).
- `stat_version`: 프로젝트 정책상 출시 전 **1 고정**(`project_stat_version_policy`), 캐시 수동 정리.

### 3.2 신규 공용 시스템 (재사용 vs 신규 명시)

| # | 시스템 | 신규/재사용 | 핵심 책임 | 적용 효과 |
|---|---|---|---|---|
| **ST** | `MonsterStatusReceiver`(몹 부착) + StatusEffect 정의 | **신규** (단, `MonsterBurnHandler` 일반화 + 아이템 스텁 흡수) | 점화/독(DoT), 서리(슬로우), 빙결·감전(정지/마비), 낙인(받피). 중첩·갱신·만료 콜백·DoT 잔여량 추적. 몹 `_agent.speed`·FSM 제어 | 불2·3·4, 얼음 전부, 전기3, 풀 전부 |
| **FD** | `PoisonFieldManager`/`GroundFieldSystem`(장판) | **신규** (풀러=`ObjectPoolerManager` 재사용) | 장판 스폰·수명·체류 판정·겹침 검출·연결 병합·플레이어 추적(빛4). 풀링·틱 부하 관리 | 풀 전부, 빛3·4 |
| **RS** | `RuneResourceState`(플레이어, `RuntimeStats` 또는 디스패처 보유) | **신규** (`ZenithGauge`/`MadnessStack`/`RelicResourceState` 패턴 참고) | 전기 스택(만료), 빛 광채 스택, 어둠 게이지(피격/적중 적립), 점화 발동 카운터 | 불2, 전기1·2, 빛1·2, 어둠 전부 |
| **AQ** | `CombatQuery` 정적 헬퍼 | **신규**(얇음, `Physics.OverlapSphere`+`IDamageable` 패턴 재사용) | 반경/원뿔/체인(가까운 N) 적 질의, 보스 식별 | 불4, 얼음4, 전기4, 빛2 |
| **DM** | 즉발 피해 경로 | **부분 재사용** | `IDamageable.TakeDamage(dmg, player, knockback:0)` 직접 호출 + 원소 VFX. 화상은 `MonsterBurnHandler.Apply` | 불1·4, 얼음4, 전기2·4, 어둠3, 빛2 |
| **SL** | `RuntimeStats` 시너지 전용 동적 레이어 | **신규 필드 추가** | `_synergyCritChance/_synergyCritDamage/_synergyAttackSpeedDynamic/_synergyAttackPctDynamic/_synergyDamageReduction` + setter. `CritChanceBonus`/`Recalculate`에 합산 | 전기1·3, 빛1·4, 어둠1·2·4 |

> **DoT/상태이상 통합 권장**: 아이템 `PoisonOnHitEffect`/`FreezeEffect`/`StunEffect`/`PetrifyEffect`(현 스텁)도 ST 시스템의 클라이언트로 이관 → 룬·아이템이 같은 상태이상 스택/저항(`DebuffResistance`)을 공유. 이게 본 작업의 최대 레버리지.

### 3.3 훅 배선 요구 (현황 대조 + 갭)

| 훅 | 현재 | 필요 조치 |
|---|---|---|
| `Tick` | ✅ `PlayerController.Update:602` | 없음 |
| `OnSkillUsed` | ✅ `ActSkillStateBase:64` | 없음. (단 "스킬 적중" 식별 필요한 얼음2는 스킬 컨텍스트를 OnHit까지 전파해야 — §5) |
| `OnHit`/`OnCrit` | ⚠ 근접만(`ColliderInstance.RaiseHit`) | **`EffectManager.OnPostDealDamage`(근/원 공통)로 룬 OnHit/OnCrit 재배선** 권장. report에 `Attacker/Target/DamageDealt/HitPosition` 존재, `isCrit`은 packet에 있음 → report에 isCrit 추가 필요할 수 있음(확인). 또는 `BasicArrow`에도 `RaiseHit` 추가(대안). |
| `OnKill` | ⚠ codeName만 | **처치 위치/타겟 좌표 전달 필요**(작열·빙하·장판 연계). `QuestEvents.OnMonsterKilled` 확장 또는 `MonsterBase.OnFatalDamage`에서 위치 포함 별도 룬 통지. ST 만료/해제 콜백이 더 정확한 트리거일 수 있음(불4·얼4는 "처치"가 아니라 "상태 만료/해제") |
| `OnDamaged` | ❌ 미발화 | **신규 배선 필수**: `PlayerController.TakeDamage`(`:95`)에서 `RuneEffects` 피격 통지 직접 호출(예: `NotifyDamaged(finalDmg, attacker)`). 어둠 속성 전제 조건. |
| (신규) `OnCrit` 분리 | OnHit과 동시 | 빛은 **치명타에서만** 트리거 → report의 isCrit로 OnCrit 분기 유지 |

추가 인터페이스 제안(IRuneEffect 확장은 최소화):
- `OnDamaged`는 이미 `IRuneEffect`에 존재 → **디스패처에 `NotifyDamaged(in HitInfo or (dmg,attacker))` 진입점 추가 + PlayerController에서 호출**만 하면 됨(인터페이스 무변경).
- "스킬 적중" 식별은 `HitInfo.ActionType`(이미 존재, `WeaponActionType`)로 근사 가능 → OnHit에서 skill 액션 여부 판별.

### 3.4 단계적 구현 우선순위 / 페이징

의존도 낮은 → 높은 순. 각 페이즈는 **그 페이즈가 요구하는 공용 시스템 선행 작업**을 포함한다.

**Phase 0 — 틀 전환 (모든 효과의 전제, 코드+데이터)**
- 단계 판정 `clusterSize → fill%`(§3.1b), `RuneEffectFactory` 24분기 골격(빈 폴백), CSV/ZoneMap 속성 재작성(§3.1e), 단계 하강 시 `OnDeactivate` 양방향, UI 4단계 표시 연동(`MerlinRuneSynergyStatusView`).
- **검증**: 룬 배치 → 각 속성 30/50/70/100% 도달 시 해당 effect_type `OnActivate` 로그 발화, 제거 시 `OnDeactivate`.

**Phase 1 — 전기 (단독완결, 인프라 최소)**
- 선행: RS(스택/게이지 컨테이너), SL(공속 동적 레이어), AQ(체인), DM. ST(감전)는 전기3에서.
- 1·2단계(스택·공속·방전 즉발)는 ST 없이 완결 → **가장 먼저 end-to-end 검증 가능한 수직 슬라이스**.
- **검증**: 적중 시 공속 스택 증가→`AttackSpeedMultiplier` 반영, 스킬 시 번개 즉발 피해, 과부하 체인 2타.

**Phase 2 — 상태이상/즉발 인프라 위 불·빛 (자기완결)**
- 선행: ST(점화=Burn 일반화), DM, AQ(원뿔/광역), RS(점화 카운터·광채 스택), SL(치확/치피). FD는 빛3·4에서.
- 불 1→4(점화 DoT·잔여량 추적·만료 폭발), 빛 1·2(치명 스택·광폭발). **빛3·4(장판)는 Phase 3 FD 이후로 분할**.
- **검증**: 점화 5스택→DoT 3틱, 만료 시 잔여합산 폭발 광역; 치명 10스택→원뿔 폭발.

**Phase 3 — 필드 시스템 위 풀 + 빛3·4 (인프라 최대)**
- 선행: FD(장판 풀링·체류·겹침·연결·추적), 보스 식별.
- 풀 1→4(독안개·겹침·연결 병합·보스 받피), 빛3·4(성역·추적 장판).
- **검증**: 장판 체류 DoT, 겹침 독×배율, 장판 연결 병합, 빛 장판 플레이어 추적·치확 부여. **성능(틱·풀) 프로파일**.

**Phase 4 — 얼음 + 어둠 (상태/게이지 + 끊긴 배선 복구)**
- 선행: ST(슬로우/빙결/해제 콜백) — Phase 2 ST 확장, **OnDamaged 배선 복구**(§1.6/3.3), 어둠 랜덤버프 풀 데이터, 지연 스케줄러(그림자 잔상).
- 얼음 1→4(서리·빙결·분쇄·빙하연쇄), 어둠 1→4(잠식·암흑·잔상·심연각성).
- **검증**: 피격 시 게이지 적립(배선 복구 확인), 빙결 해제 시 빙하 광역+서리, 암흑 버프 토글, 심연각성 랜덤 추첨.

> 의존 핵심: **RS+SL+DM+AQ → 전기/불/빛 리소스부 → ST → 얼음/풀 → FD → 풀/빛장판 → OnDamaged복구 → 어둠.** ST와 FD가 최대 병목.

### 3.5 리스크 / 모호점

1. **단계 판정 = fill% vs 연결성**: 스펙은 순수 점유율. 코드 자산(`ZoneClusterCalculator`)은 연결 클러스터. **연결성을 단계에 반영할지(보드 빌딩 깊이 ↑) 순수 점유율인지 확정 필요.** 본 설계는 순수 점유율 가정. (연결성을 별도 보너스로 둘 수도 있음 — CENTER처럼.)
2. **누적형 정확한 의미**: 상위 단계가 하위를 "포함"이 (a) 하위 효과 그대로 유지 + 상위 추가인지, (b) 상위가 하위를 대체/강화인지. 본 설계는 (a) 가정(스펙 문맥상 폭염=점화 전제). 빌드 시 하위 비활성 시점(편집 중 단계 하강) UX 미정.
3. **"공격력 N%" 기준값**: melee/ranged 분리 스탯에서 어느 값인가. 본 설계 `GetEffectiveAttack`(무기 타입) 가정. 즉발 피해가 방어 적용 전/후인지도 미정(`MonsterBase.TakeDamage`가 방어 차감) — DoT/즉발이 방어를 받으면 8%/16% 수치가 과소해질 수 있음 → **방어 무시 옵션 검토**.
4. **OnKill vs 상태 만료**: 불4·얼4는 "처치"가 아니라 "점화 만료"/"빙결 해제"가 트리거. 현 `OnKill`(codeName만)로는 부족 → **ST 콜백 기반 트리거가 정답**. OnKill은 보조.
5. **단일 슬롯 스탯 충돌**: 크릿/캐릭터 배율 단일 소유자(가웨인·랜슬롯 유물)와 시너지 동적 버프 충돌 → SL 신규 레이어로 분리 필수(설계 반영). 합산 상한(치확 100%, 받피 등) 클램프 정책 필요.
6. **보스 특수(받피+15%, 풀4)**: 보스 식별 수단 확인 필요(보스 전용 컴포넌트/태그). `BossRoomController`/보스 클래스 식별자 활용.
7. **성능**: 장판 풀링·겹침 N² 판정·DoT 다중 틱·체인 OverlapSphere가 다수 몹 + 다수 장판에서 프레임 비용 위험. FD는 **틱 주기 분산 + 겹침 공간 분할(그리드 해싱)** 설계 권장. DoT는 `MonsterBurnHandler`식 컴포넌트-당-몹 모델 유지(스폰 비용 vs 중앙 관리 트레이드오프).
8. **데이터 이원화 잔존**: 구 `SynergyMechanicsState`/`ApplySynergyMechanicEffect`(스탯존)와 신 속성 효과가 `ApplyMechanicEffect`에서 **둘 다 호출**됨. 속성 effect_type은 구 switch에 안 걸려 무해하나, **혼선 방지 위해 구 경로 정리 시점 결정 필요**(범위 외, 별도 작업).
9. **밸런스 전면 미검증**: 모든 수치(8%/16%/스택 상한/장판 개수)는 기획 1차값. 밸런싱 반복 잦으면 §3.1c B안(params_json) 재고.
10. **세이브/복원**: 단계는 룬 점유 셀에서 재계산(`MerlinRuneBridge.RestoreRuneCells`가 권위). 런타임 리소스(게이지/스택)는 휘발 가정 — 씬 전환 시 리셋 정책 확인(`SynergyRecord`는 단계 활성만 기록).

---

## 4. 보고 요약

- **데이터 모델**: 기존 `RuneSynergyEntry` 스키마 유지, `threshold` 의미만 **클러스터 셀 수 → 채움%(30/50/70/100)** 로 재정의. effect_type = (속성×단계) 24 고유키, 팩토리 switch 분기. 파라미터는 value*~duration **위치 규약 + 코드 상수**(A안). CSV/ZoneMap을 구 스탯존 → 6속성으로 재작성(+Addressables 폴백 동기화).
- **신규 공용 시스템**: ① StatusEffect(`MonsterStatusReceiver`, 화상/아이템 스텁 흡수) ② 장판/필드(`PoisonFieldManager`) ③ 플레이어 리소스(`RuneResourceState`: 스택/게이지) ④ 광역·체인 타겟질의(`CombatQuery`) ⑤ 즉발 피해 경로(부분 재사용) ⑥ `RuntimeStats` 시너지 전용 동적 스탯 레이어(SL).
- **훅 갭(필수 복구 2)**: ① **OnDamaged 미발화** — 몹이 `RaiseHit` 안 탐 → `PlayerController.TakeDamage`에서 직접 통지 추가(어둠 전제). ② **원거리 OnHit/OnCrit 미발화** — `BasicArrow`가 `RaiseHit` 안 탐 → 룬 OnHit/OnCrit을 근/원 공통 `EffectManager.OnPostDealDamage`로 재배선. 추가로 OnKill에 처치 위치 필요(불4·얼4는 ST 만료 콜백이 더 정확).
- **우선순위 페이즈**: P0 틀전환(fill% 판정+팩토리골격+데이터) → P1 전기(단독완결) → P2 불·빛 리소스부(ST+DM) → P3 풀·빛장판(FD) → P4 얼음·어둠(ST확장+OnDamaged복구). 병목 = ST·FD.
- **최대 리스크**: ① 단계 판정 모델(점유율 vs 연결성) 기획 확정 ② 즉발/DoT의 방어 적용 여부(수치 과소 위험) ③ 단일 슬롯 스탯 충돌(SL로 분리) ④ 장판/DoT/체인 **성능** ⑤ 누적형·편집 중 단계 하강 UX 정의.

**게임플레이 코드 미작성. 본 문서만 생성. 커밋/푸시 안 함.**
