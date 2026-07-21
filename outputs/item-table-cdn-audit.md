# 아이템 테이블 / CDN 사전조사 (신규 아이템 기획용)

조사일 2026-07-19 · 읽기 전용(수정·커밋 없음)
목적: 레거시 아이템 정리 후 **프레임워크만 유지 + 기본 아이템만 남기고 재기획**하기 위한 판단 재료

---

## 0. 결론 요약

| 질문 | 답 |
|---|---|
| 바탕화면에 아이템 CDN 테이블이 몇 개? | **1개뿐** — `ITEM_DATA.csv`. 별도 CDN 파일·백업본 없음 |
| 프로젝트 ITEM_DATA와 같은 것? | **같은 세대**. 프로젝트엔 CSV 없고 `Resources/ITEM_DATA.json`(생성물)만 존재. 192행 / effect_type 분포 완전 일치 |
| 정본은? | **작성 정본 = 바탕화면 CSV**, **런타임 정본 = 뒤끝 CDN 차트 236201**. JSON은 오프라인 폴백 |
| effect_type 어휘 갭? | **갭 0**. CSV의 54종이 레지스트리 107키에 전부 등록됨. 여유분 52키 |
| 진짜 레거시는? | **CSV가 아니라 `SOdata/` 56개 동화 이름 .asset**. CSV id와 교집합 **0** — 완전히 버려진 이전 세대 |

가장 중요한 발견: **버릴 레거시와 남길 뼈대의 경계가 예상과 다릅니다.** 아래 4절 참조.

---

## 1. 바탕화면 CDN 파일 목록·스키마

경로: `C:\Users\u\Desktop\사용중 테이블\`

### 1-1. 아이템 관련 파일 — 1개뿐

| 파일 | 크기 | 수정시각 | 행수 |
|---|---|---|---|
| `ITEM_DATA.csv` | 23,481 B | 2026-07-15 20:41 | 193줄 (헤더 1 + 데이터 **192**) |

- 하위 폴더 `정정본\` (2026-07-18 23:36) — **비어 있음**. 아이템 관련 파일 없음
- 폴더 내 다른 20개 CSV(룸풀·대사·장비 등) 중 아이템 테이블은 위 1개가 유일
- "CDN 파일"로 따로 존재하는 것은 없음 — 이 CSV가 뒤끝 CDN에 업로드되는 원본

### 1-2. 스키마 (14열)

```
passive_id, item_name, slot, effect_type, trigger, value, value2, value3,
max_stack, duration, description, stat_version, shape_id, grade
```
근거: `사용중 테이블\ITEM_DATA.csv:1` (BOM 포함)

예시 행 (`ITEM_DATA.csv:2`):
```
item_t1_dull_blade,힘의 룬,1,AllDamage,Always,4,0,0,0,0,공격력 +4,1,1,Common
```

### 1-3. 데이터 구조

- **192행 = 153개 아이템** — 39개 아이템이 효과 2행 이상(멀티 효과)
  - 예: `item_t2_strike_ring`(강타의 룬) = AllDamage 0.15 + CritDamage 0.15
- **티어 ↔ 등급 1:1 고정**

| id 접두 | grade | 행수 |
|---|---|---|
| `item_t1_*` | Common | 60 |
| `item_t2_*` | Rare | 75 |
| `item_t3_*` | Epic | 35 |
| `item_t4_*` | Legendary | 22 |

- **네이밍이 이미 룬 테마**: 전부 `○○의 룬` (힘의 룬, 방벽의 룬, 활력의 룬…). 동화 이름 아님
- `shape_id` 사용값: 1(60) 2(75) 4(13) 8(15) 10(4) 11(5) 13(17) 18(3) — 20종 모양 중 8종만 사용
- `stat_version` 전 행 1 (프로젝트 정책과 일치)

---

## 2. 프로젝트 ITEM_DATA와의 대조

### 2-1. 프로젝트 측 파일 — CSV는 존재하지 않음

| 위치 | 형식 | 크기 | 수정시각 | 내용 |
|---|---|---|---|---|
| 바탕화면 `ITEM_DATA.csv` | CSV | 23,481 B | 07-15 20:41 | 192행 |
| `Assets/RelicFairy/Resources/ITEM_DATA.json` | JSON | 74,544 B | 07-17 02:35 | **192** items |

프로젝트 `Assets/` 전체에 `ITEM_DATA*.csv` **없음**. JSON 단일.

### 2-2. 내용 대조 — 동일 세대

| 항목 | 바탕화면 CSV | 프로젝트 JSON | 판정 |
|---|---|---|---|
| 행/아이템 수 | 192 | 192 | 일치 |
| id 체계 | `item_t1~t4_*` | `item_t1~t4_*` | 일치 |
| effect_type 분포 | 54종 (AllDamage 21, Defense 15, CondAllDamage 14…) | 54종 (**히스토그램 완전 동일**) | 일치 |
| 스키마 | 14열 | 동일 14키 + `item_id` 추가 | JSON이 `item_id` 파생 |

JSON 키: `item_id, passive_id, item_name, slot, effect_type, trigger, value, value2, value3, max_stack, duration, description, stat_version, shape_id, grade`

→ JSON은 CSV에서 생성된 **미러**. `item_id`는 `passive_id` 복제(`ItemDataManager.cs:46-48`이 빈 `item_id`를 `passive_id`로 채우는 호환 코드 보유).
JSON 수정시각(07-17)이 CSV(07-15)보다 뒤 = CSV → JSON 생성 순서와 일치. **버전 어긋남 없음.**

### 2-3. 정본 판단 — 런타임 로드 경로

`Assets/RelicFairy/Systems/Managers/Scripts/DataManagers/ItemDataManager.cs:27-56`

```
1) LoadFromJson()            — 로컬 캐시 파일 있으면 선로드
2) LoadFromServerAsync()     — 뒤끝 CDN 차트 236201  ← 최우선 정본
3) _itemById.Count == 0 이면 — Addressables "ITEM_DATA" (Resources JSON) 폴백
```
- 차트 ID `236201`: `ItemDataManager.cs:18`

**정본 결론**
- **작성 정본 = 바탕화면 `ITEM_DATA.csv`** (사람이 편집하는 유일 원본)
- **런타임 정본 = 뒤끝 CDN 236201** (온라인이면 항상 이쪽이 이김)
- `Resources/ITEM_DATA.json` = 오프라인/에디터 폴백 전용
- ⚠️ *추정*: CDN 업로드본이 07-15 CSV와 동일한지는 오프라인에서 검증 불가. 기존 메모(`project_item_data_load_path`)의 "CSV 변경 시 CDN 재업로드 + 폴백 JSON 재생성 둘 다 필요" 원칙이 그대로 적용됩니다.

---

## 3. effect_type 어휘 대조 — **갭 0**

레지스트리: `Assets/RelicFairy/Systems/Item/Core/ItemEffectRegistry.cs` (199줄, 등록 키 **107**개)

| 집합 | 개수 |
|---|---|
| CSV가 사용하는 effect_type | **54** |
| 레지스트리 등록 키 | **107** |
| **CSV에 있는데 레지스트리에 없음 (갭)** | **0** ✅ |
| 레지스트리에만 있음 (미사용 여유분) | **52** |

→ **바탕화면 테이블의 effect_type은 100% 그대로 재사용 가능.** 새 기획에서 어휘를 새로 만들 필요 없음.

### 3-1. CSV 사용 54종 (빈도순)

**스탯계(무조건)** AllDamage 21 · Defense 15 · MaxHP 11 · MaxHPPercent 6 · DefensePercent 6 · SkillCooldownReduction 5 · CritDamage 5 · MoveSpeed 4 · CritChance 4 · AttackSpeed 4 · SkillDamage 2 · ProjectilePierce 2 · ProjectileCount 2

**조건부(Cond\*)** CondAllDamage 14 · CondDefensePercent 9 · CondMoveSpeed 6 · CondCritChance 6 · CondAttackSpeed 5 · CondMaxHpPercent 4 · CondCritDamage 4 · CondSkillDamage 1

**특수 메커닉(T3/T4)** FirstHitBonus 3 · TimedEmpowerNext · TargetVulnStack · StackDamagePerTarget · SkillProjectileCount · SkillIdleEmpower · SkillCastGuard · RepeatChance · MeleeShapeCircle · MeleeRangeExtend · MeleeMultiHit · MarkExplode · LastBreath · JustGuard · ExecuteBonus · EchoStrike · DoubleHitTiming · DamageAccumEmpower · CombatStartWindow · ChargeWhileIdle · AttackInterrupt (각 2) · StationaryRangeBuff · SplitStrike · SkillReadyEmpower · SkillCdReset · RoomEntryWindow · NoHitThenCrit · HpThresholdAoE · GuaranteedCrit · EchoArrow · DamageAccumPenetrate · CritMomentum · CounterShockwave · BrandChain (각 1)

### 3-2. 레지스트리 여유분 52종 (구현돼 있으나 테이블 미사용)

신규 기획에서 **CSV 한 줄만 쓰면 바로 동작하는** 재고입니다:

- **자원/유틸** Luck · GoldGain · GoldOnKill · ShopRoomChance · SpecialRoomChance · HighGradeItemChance · ItemGradeUp · ConsumableSlot
- **생존** DamageReduction · DamageNegate · DeathNegate · ReviveHeal · Heal · HealingReceived · HPRegenOnHit · HPRegenOnClear · HPRegenOnBossEnter · DebuffResistance · DebuffDuration
- **반격/상태이상** DamageReflect · FireReflect · Freeze · Stun · Petrify · PoisonOnHit · PoisonApple · ExtraAttack · ExtraDamageOnHit · DefenseOnHit
- **회피/구르기** RollCooldown · RollDistance · RollLandingDamage · FirstAttackAfterRoll · TeleportSwap
- **스킬** SkillCooldownFlat · FireExplosionOnSkill · LightningOnSkill
- **기타 스탯** AttackDamage · AllStats · AllElementBonus · RangedRange · CondAttackPercent · DefensePermStack · MaxHPDecreasePerRoom · RandomElement
- **보스/제작** BossDropItem · BuffRefreshOnBoss · FullHealOnKill · HPRegenOnRecipe · AllDamageOnRecipe · RecipeSynergyNextAttack

⚠️ *추정*: 이 52종은 "레지스트리에 등록됨"만 확인했고, 각 구현체의 실제 동작 품질(스텁/근사 여부)은 이번 조사 범위 밖입니다.

---

## 4. 유지/폐기 구분 제안

### 4-1. ⚠️ 먼저 짚을 것 — 진짜 레거시는 CSV가 아닙니다

`Assets/RelicFairy/Shared/Item/SOdata/**/*.asset` 에 **56개** ScriptableObject가 있고, id가 전부 동화 이름입니다:
`item_snow_white_apple`, `item_cinderella_shoes`, `item_excalibur_fragment`, `item_dragon_heart`, `item_aladdin_lamp` …

**CSV의 153개 id와 교집합 = 0.**

즉 아이템은 **두 세대가 공존**합니다:

| 세대 | 위치 | 규모 | 상태 |
|---|---|---|---|
| **구세대(동화)** | `SOdata/` .asset 56개 | 56 | **데이터 백업 없는 고아**. CSV에 대응 행 없음 → 로드돼도 효과 데이터 0. **진짜 레거시** |
| **현세대(룬)** | 바탕화면 CSV + Resources JSON | 153 | 스키마·어휘 모두 현행 프레임워크와 정합 |

→ "기존 192아이템이 게임에 도달한 적 없다"는 앞선 결론은, **원인이 CSV 품질이 아니라 SO 세대 불일치일 가능성**이 큽니다. 정리 대상 1순위는 CSV가 아니라 `SOdata/` 56개 동화 에셋입니다.
⚠️ *추정* — SO ↔ CSV 연결이 왜 끊겼는지(생성기 미실행인지 의도적 폐기인지)는 `Assets/Editor/GenerateItemSOFromCSV.cs` 동작 확인이 별도로 필요합니다. **삭제 전 사용자 확인 필수.**

### 4-2. 남길 "기본 아이템" 후보 — 스탯형 코어 (권장 시작점)

선별 기준: `trigger = Always` + 순수 스탯 effect_type + T1/T2 (Common/Rare)

**11개 스탯 축이 이미 균형 있게 채워져 있습니다:**

| effect_type | T1(Common) | T2(Rare) | 값 범위 | 유지 판단 |
|---|---|---|---|---|
| AllDamage | 8종 | 9종 | +3~16 / +6%~20% | **유지** — 핵심 축 |
| Defense | 7종 | 6종 | +2~16 | **유지** |
| MaxHP | 5종 | 4종 | +10~60 | **유지** |
| MaxHPPercent | 1종 | 3종 | 8%~24% | **유지** |
| DefensePercent | 1종 | 3종 | 6%~20% | **유지** |
| CritChance | 2종 | 2종 | 4%~10% | **유지** |
| CritDamage | 2종 | 3종 | 12%~28% | **유지** |
| AttackSpeed | 2종 | 2종 | 8%~16% | **유지** |
| MoveSpeed | 3종 | 1종 | 9%~20% | **유지** |
| SkillCooldownReduction | 3종 | 2종 | 8%~20% | **유지** |
| SkillDamage | 1종 | 1종 | 7%~12% | **유지** |

- 해당 행 수: **83행** (전체 192행 중 43%)
- 이 중 39개 멀티효과 아이템(예: 코어의 룬 = AllDamage 9 + Defense 9)이 "복합 스탯" 역할을 이미 수행

**뼈대로 남길 최소 세트 제안: 스탯 11축 × T1/T2 = 약 40~50개 아이템.**
근거 — (a) effect_type이 전부 레지스트리 등록·갭 0, (b) 값 밸런싱이 티어별로 이미 정렬돼 있음, (c) 조건부·특수 메커닉이 아니라 검증 부담이 가장 낮음.

### 4-3. 판단 보류 — 조건부(Cond\*) 49행

CondAllDamage 14 · CondDefensePercent 9 · CondMoveSpeed 6 · CondCritChance 6 · CondAttackSpeed 5 · CondMaxHpPercent 4 · CondCritDamage 4 · CondSkillDamage 1

- 구현체 `ConditionalStatBuffEffect.cs` 단일 클래스가 전부 처리 → **유지 비용 낮음**
- 사용 trigger 어휘 20종: `HPBelow30/40/50 · AfterSkill · AfterRoomEnter · WhileMoving · SameTarget · NoHit · HasShield · DuringBoss · AfterHit · WhileSkillCooldown · Stationary · SingleEnemy · EnemiesNearby · DefenseAbove · Consecutive · FirstAttackInRoom` 등
- **제안: trigger 어휘는 유지, 개별 아이템은 재기획 시 재배치.** 조건부는 "기본 아이템"이 아니라 2단계 재미 요소이므로 신규 기획의 설계 의도에 맞춰 다시 짜는 편이 낫습니다.

### 4-4. 폐기/재검토 후보

| 대상 | 규모 | 제안 | 이유 |
|---|---|---|---|
| `SOdata/` 동화 .asset | **56개** | **폐기 검토 1순위** (⚠️삭제 전 확인) | CSV 교집합 0, 데이터 백업 없는 고아 |
| T3/T4 특수 메커닉 아이템 | 57행 (Epic 35 + Legendary 22) | **효과는 유지, 아이템은 재기획** | effect_type 34종은 전부 구현 완료 자산. 다만 아이템 1개당 고유 메커닉 1개 구조라 신규 기획 컨셉에 종속 |
| shape_id 미사용 12종 | — | 신규 기획에서 활용 여지 | 20종 중 8종만 사용 중 |

---

## 5. 신규 기획 출발점 정리

**그대로 가져갈 자산**
1. effect_type 어휘 **107종**(사용 54 + 여유 52) — 코드 수정 없이 CSV로 조합 가능
2. trigger 어휘 20종 + `ConditionalStatBuffEffect` 조건부 프레임워크
3. 14열 스키마 + 멀티행(아이템 1개 = 효과 N행) 구조
4. 티어↔등급 1:1 + 티어별 값 밸런싱 레퍼런스 (4-2 표)

**정리 대상**
1. `SOdata/` 동화 .asset 56개 (⚠️ 사용자 확인 후)
2. CSV 아이템 목록 자체 — 스탯형 코어 ~40~50개만 남기고 나머지 재기획

**후속 확인 필요 (이번 조사 범위 밖)**
- `GenerateItemSOFromCSV.cs` 가 왜 CSV id로 SO를 못 만들었는지 → SO 세대 불일치 원인
- CDN 236201 업로드본이 07-15 CSV와 동일한지
- 여유분 52 effect_type 구현체의 스텁/근사 여부

---

### 근거 파일 경로
- `C:\Users\u\Desktop\사용중 테이블\ITEM_DATA.csv` (193줄)
- `Assets/RelicFairy/Resources/ITEM_DATA.json` (192 items)
- `Assets/RelicFairy/Systems/Item/Core/ItemEffectRegistry.cs:57-195` (등록 키 107)
- `Assets/RelicFairy/Systems/Managers/Scripts/DataManagers/ItemDataManager.cs:18` (차트 236201) / `:27-56` (로드 우선순위)
- `Assets/RelicFairy/Systems/Item/Effects/Conditional/ConditionalStatBuffEffect.cs`
- `Assets/RelicFairy/Shared/Item/SOdata/**/*.asset` (56개)
